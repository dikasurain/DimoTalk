using DimoTalk.Maui.Services;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 语音对话编排器：状态机驱动，单一 ContinuousAudioCapture 持续录音
///
/// 状态流：
/// Idle → Listening（唤醒词监听）
///      → Recording（唤醒触发/热对话/VAD barge-in）
///      → Processing（ASR + GPT）
///      → Reply（TTS 播放，期间 barge-in 可打断）
///      → HotConversation（8s 窗口，VAD 触发直接 → Recording）
///      → Listening（热对话超时）
///
/// 全程 AudioRecord 不重启，零延迟切换
/// </summary>
public class VoiceConversationManager : IAsyncDisposable
{
    private readonly ContinuousAudioCapture _capture;
    private readonly IWakeWordDetector _wakeWordDetector;
    private readonly IAsrService _asr;
    private readonly ITtsService _tts;
    private readonly IAudioPlayer _player;
    private readonly ChatService _chatService;
    private readonly VoiceRecorder _recorder;

    private VoiceState _state = VoiceState.Idle;
    private CancellationTokenSource? _cts;
    private readonly object _stateLock = new();
    private bool _disposed;

    // Barge-in / 热对话窗口
    private System.Timers.Timer? _hotWindowTimer;
    private CancellationTokenSource? _replyCts;
    private const int HotWindowSeconds = 8;

    public VoiceState State
    {
        get { lock (_stateLock) return _state; }
    }

    public event EventHandler<VoiceState>? StateChanged;
    public event EventHandler<string>? StatusMessage;

    public VoiceConversationManager(
        ContinuousAudioCapture capture,
        IWakeWordDetector wakeWordDetector,
        IAsrService asr,
        ITtsService tts,
        IAudioPlayer player,
        ChatService chatService,
        VoiceRecorder recorder)
    {
        _capture = capture;
        _wakeWordDetector = wakeWordDetector;
        _asr = asr;
        _tts = tts;
        _player = player;
        _chatService = chatService;
        _recorder = recorder;
        _recorder.RecordingCompleted += OnRecordingCompleted;
    }

    public void SetState(VoiceState state)
    {
        lock (_stateLock)
        {
            if (_state == state) return;
            _state = state;
        }
        StateChanged?.Invoke(this, state);
    }

    private void EmitStatus(string msg)
    {
        StatusMessage?.Invoke(this, msg);
    }

    // ─────────── 启动 / 停止 ───────────

    public async Task StartAsync(string userId, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 1️⃣ 启动单一音频采集源（持有麦克风，全程不释放）
        await _capture.StartAsync();

        // 2️⃣ 设置唤醒词
        var wakeWord = Preferences.Default.Get("wake_word", "滴墨");
        _wakeWordDetector.WakeWord = wakeWord;

        // 3️⃣ 解压 Vosk 模型（首次）
        if (_wakeWordDetector is VoskWakeWordDetector vosk)
            await vosk.EnsureModelExtractedAsync();

        // 4️⃣ 进入 Listening
        await EnterListeningAsync();
        EmitStatus($"侧耳聆听 · 唤之曰「{wakeWord}」");
    }

    public async Task StopAsync()
    {
        CancelHotWindow();

        // 打断 TTS 播放
        try { _replyCts?.Cancel(); } catch { }

        // 停录音
        try { await _recorder.StopAsync(); } catch { }

        // 停唤醒词
        try { await _wakeWordDetector.StopAsync(); } catch { }

        // 停音频采集
        try { await _capture.StopAsync(); } catch { }

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        SetState(VoiceState.Idle);
    }

    // ─────────── 状态切换 ───────────

    private async Task EnterListeningAsync()
    {
        SetState(VoiceState.Listening);

        // 取消之前的录音订阅
        try { await _recorder.StopAsync(); } catch { }

        // 启动唤醒词检测（订阅同一个 AudioCapture）
        await _wakeWordDetector.StartAsync(OnWakeOrHotTriggeredAsync, _cts?.Token ?? default);
    }

    private async Task EnterHotConversationAsync()
    {
        SetState(VoiceState.HotConversation);
        CancelHotWindow();

        // 启动 8 秒热对话定时器
        _hotWindowTimer = new System.Timers.Timer(HotWindowSeconds * 1000) { AutoReset = false };
        _hotWindowTimer.Elapsed += async (_, _) =>
        {
            CancelHotWindow();
            if (State == VoiceState.HotConversation)
                await EnterListeningAsync();
        };
        _hotWindowTimer.Start();

        // 同时订阅 VAD —— 检测到语音直接开始录音
        _capture.Subscribe(OnHotConversationVadPcm);
        EmitStatus("热对话中 · 直接说话");
    }

    private void CancelHotWindow()
    {
        try { _hotWindowTimer?.Stop(); _hotWindowTimer?.Dispose(); } catch { }
        _hotWindowTimer = null;
        _capture.Unsubscribe(OnHotConversationVadPcm);
    }

    private DateTime _hotLastSpeech = DateTime.MinValue;
    private void OnHotConversationVadPcm(byte[] pcm)
    {
        // 简单 VAD：RMS 超过阈值即触发
        double sum = 0;
        var samples = pcm.Length / 2;
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short s = (short)(pcm[i] | (pcm[i + 1] << 8));
            sum += s * s;
        }
        double rms = Math.Sqrt(sum / Math.Max(samples, 1));
        if (rms > 500) _hotLastSpeech = DateTime.Now;

        // 连续 300ms 检测到语音 → 触发录音
        if ((DateTime.Now - _hotLastSpeech).TotalMilliseconds < 300 && rms > 500)
        {
            CancelHotWindow();
            _ = StartRecordingNowAsync(preRoll: 1);
        }
    }

    /// <summary>唤醒词触发 或 热对话 VAD 触发 → 进入录音</summary>
    private async Task OnWakeOrHotTriggeredAsync()
    {
        if (State != VoiceState.Listening && State != VoiceState.HotConversation) return;
        await StartRecordingNowAsync(preRoll: State == VoiceState.Listening ? 1 : 0);
    }

    private async Task StartRecordingNowAsync(int preRoll)
    {
        SetState(VoiceState.Recording);
        CancelHotWindow();

        // 停止唤醒词检测（释放它的 Vosk 订阅，但 AudioCapture 不关）
        try { await _wakeWordDetector.StopAsync(); } catch { }

        // 播放叮声提示（异步，不阻塞录音启动）
        _ = PlayPromptToneAsync();

        // 开始录音（订阅 AudioCapture，回填 preRoll 秒历史）
        await _recorder.StartRecordingAsync(preRollSeconds: preRoll);
    }

    // ─────────── 录音完成回调 ───────────

    private async void OnRecordingCompleted(object? sender, byte[] wavBytes)
    {
        try
        {
            SetState(VoiceState.Processing);
            EmitStatus("研墨构思中…");

            // 1. ASR
            string text;
            try
            {
                text = await _asr.RecognizeAsync(wavBytes, _cts?.Token ?? default);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ASR 失败: {ex.Message}");
                text = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                // 没识别到内容 → 回热对话窗口（还在监听）
                await EnterHotConversationAsync();
                return;
            }

            // 2. GPT（语音对话强制走闲聊链路：完整记忆 + 方言润色）
            var reply = await _chatService.SendMessageAsync(GetUserId(), text, forceCasual: true);

            // 3. TTS
            SetState(VoiceState.Reply);
            EmitStatus("答曰…");

            _replyCts = new CancellationTokenSource();
            var ttsAudio = await _tts.SynthesizeAsync(reply, _replyCts.Token);

            if (_replyCts.IsCancellationRequested)
            {
                // 用户打断了 TTS → 跳过播放，直接进热对话
                _replyCts.Dispose();
                _replyCts = null;
                await EnterHotConversationAsync();
                return;
            }

            // 4. 播放 TTS（后台监听 barge-in）
            await PlayWithBargeInAsync(ttsAudio, _replyCts.Token);

            if (_replyCts.IsCancellationRequested)
            {
                _replyCts.Dispose();
                _replyCts = null;
                // barge-in 触发后 OnBargeInDetected 会处理进入 Recording
                return;
            }

            try { _replyCts.Dispose(); } catch { }
            _replyCts = null;

            // 5. 播放完毕 → 进入热对话窗口（8 秒内 VAD 触发直接录音，无需唤醒词）
            await EnterHotConversationAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"语音对话失败: {ex}");
            EmitStatus($"出错了：{ex.Message}");
            await EnterListeningAsync();
        }
    }

    // ─────────── TTS 播放 + Barge-in ───────────

    private async Task PlayWithBargeInAsync(byte[] audioBytes, CancellationToken ct)
    {
        var bargeInTriggered = false;

        void OnBargeIn(byte[] pcm)
        {
            if (ct.IsCancellationRequested || bargeInTriggered) return;
            double sum = 0;
            var samples = pcm.Length / 2;
            for (int i = 0; i < pcm.Length; i += 2)
            {
                short s = (short)(pcm[i] | (pcm[i + 1] << 8));
                sum += s * s;
            }
            double rms = Math.Sqrt(sum / Math.Max(samples, 1));
            // TTS 播放期间，环境 RMS 突然升高 → 用户开口打断
            if (rms > 1200)
            {
                bargeInTriggered = true;
                _replyCts?.Cancel();
            }
        }

        // 注意：ContinuousAudioCapture 一直开着，这里只是临时订阅做 barge-in 检测
        _capture.Subscribe(OnBargeIn);

        try
        {
            await _player.PlayAsync(audioBytes, ct);
        }
        catch (OperationCanceledException) { /* barge-in 打断 */ }
        finally
        {
            _capture.Unsubscribe(OnBargeIn);
        }

        if (bargeInTriggered)
        {
            // 用户开口打断了 TTS → 直接开始录音
            await StartRecordingNowAsync(preRoll: 1);
        }
    }

    // ─────────── 辅助 ───────────

    private async Task PlayPromptToneAsync()
    {
        var sampleRate = 16000;
        var durationMs = 100;
        var samples = sampleRate * durationMs / 1000;
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * 880 * t) * 10000);
            bytes[i * 2] = (byte)(sample & 0xFF);
            bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray()); writer.Write(36 + bytes.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray()); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write("data".ToCharArray()); writer.Write(bytes.Length);
        writer.Write(bytes);

        await _player.PlayPromptAsync(ms.ToArray());
    }

    private static string GetUserId() =>
        Preferences.Default.Get("user_id", Guid.NewGuid().ToString());

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        if (_wakeWordDetector is IAsyncDisposable d) await d.DisposeAsync();
        if (_player is IDisposable dp) dp.Dispose();
        _recorder.Dispose();
    }
}
