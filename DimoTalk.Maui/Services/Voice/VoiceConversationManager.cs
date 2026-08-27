using DimoTalk.Maui.Services;
using NAudio.Wave;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 语音对话编排器：状态机驱动
/// Idle → Listening（唤醒词检测）→ Recording（用户问答录制）→ Processing（ASR+GPT）→ Reply（TTS）
/// </summary>
public class VoiceConversationManager : IAsyncDisposable
{
    private readonly IWakeWordDetector _wakeWordDetector;
    private readonly IAsrService _asr;
    private readonly ITtsService _tts;
    private readonly IAudioPlayer _player;
    private readonly ChatService _chatService;
    private readonly VoiceRecorder _recorder = new();

    private VoiceState _state = VoiceState.Idle;
    private CancellationTokenSource? _cts;
    private readonly object _stateLock = new();
    private bool _disposed;

    public VoiceState State
    {
        get { lock (_stateLock) return _state; }
    }

    public event EventHandler<VoiceState>? StateChanged;

    public VoiceConversationManager(
        IWakeWordDetector wakeWordDetector,
        IAsrService asr,
        ITtsService tts,
        IAudioPlayer player,
        ChatService chatService)
    {
        _wakeWordDetector = wakeWordDetector;
        _asr = asr;
        _tts = tts;
        _player = player;
        _chatService = chatService;
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

    public async Task StartAsync(string userId, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(VoiceState.Listening);

        await _wakeWordDetector.StartAsync(OnWakeWordDetected, _cts.Token);
    }

    /// <summary>
    /// 唤醒词触发：播放提示音 + 切换到录音状态
    /// </summary>
    private async Task OnWakeWordDetected()
    {
        if (State != VoiceState.Listening) return;

        SetState(VoiceState.Recording);
        await _wakeWordDetector.StopAsync();

        // 提示音（叮）
        await PlayPromptToneAsync();

        // 开始录音（持续到静音 2 秒）
        _recorder.StartRecording();
    }

    /// <summary>
    /// 录音完成 → ASR → GPT → TTS → 播放 → 回到 Listening
    /// </summary>
    private async void OnRecordingCompleted(object? sender, byte[] wavBytes)
    {
        try
        {
            SetState(VoiceState.Processing);

            // 1. ASR
            var text = await _asr.RecognizeAsync(wavBytes, _cts?.Token ?? default);
            if (string.IsNullOrWhiteSpace(text))
            {
                await ReenterListeningAsync();
                return;
            }

            // 2. GPT
            var reply = await _chatService.SendMessageAsync(GetUserId(), text);

            // 3. TTS
            SetState(VoiceState.Reply);
            var audio = await _tts.SynthesizeAsync(reply, _cts?.Token ?? default);

            // 4. 播放
            await _player.PlayAsync(audio, _cts?.Token ?? default);

            // 5. 回到监听唤醒词状态
            await ReenterListeningAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"语音对话失败: {ex}");
            await ReenterListeningAsync();
        }
    }

    private async Task ReenterListeningAsync()
    {
        SetState(VoiceState.Listening);
        if (_wakeWordDetector is VoskWakeWordDetector vosk)
        {
            await vosk.StartAsync(OnWakeWordDetected, _cts?.Token ?? CancellationToken.None);
        }
    }

    private async Task PlayPromptToneAsync()
    {
        // 简单：用 sine 波生成 880Hz 短音 100ms（避免依赖外部资源文件）
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

        // 转 WAV
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + bytes.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(bytes.Length);
        writer.Write(bytes);

        await _player.PlayPromptAsync(ms.ToArray());
    }

    private static string GetUserId() =>
        Preferences.Default.Get("user_id", Guid.NewGuid().ToString());

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        if (_wakeWordDetector is IAsyncDisposable d) await d.DisposeAsync();
        if (_player is IDisposable dp) dp.Dispose();
    }
}
