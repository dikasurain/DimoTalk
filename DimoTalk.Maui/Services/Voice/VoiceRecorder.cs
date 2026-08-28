namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 录音服务 — 订阅 ContinuousAudioCapture 的 PCM 流，支持 VAD 静音断句 + 历史回填
/// </summary>
public class VoiceRecorder : IDisposable
{
    public event EventHandler<byte[]>? RecordingCompleted;
    public event EventHandler<double>? AudioLevelChanged;
    public bool IsRecording { get; private set; }

    private readonly ContinuousAudioCapture _capture;
    private readonly List<byte> _buffer = new();
    private DateTime _lastSpeech = DateTime.MinValue;
    private System.Timers.Timer? _vadTimer;
    private readonly object _bufLock = new();

    private const int SilenceTimeoutSeconds = 1500; // ms，降低阈值提高响应
    private const int MaxRecordingSeconds = 30;
    private const double SpeechThreshold = 500; // RMS

    private DateTime _startTime;

    public VoiceRecorder(ContinuousAudioCapture capture)
    {
        _capture = capture;
    }

    /// <summary>
    /// 开始录制。可选回填历史音频秒数（捕获唤醒被截断的语音开头）。
    /// </summary>
    public async Task StartRecordingAsync(int preRollSeconds = 0)
    {
        if (IsRecording) return;
        IsRecording = true;

        lock (_bufLock) _buffer.Clear();
        _lastSpeech = DateTime.Now;
        _startTime = DateTime.Now;

        // 回填历史
        if (preRollSeconds > 0)
        {
            var history = _capture.GetHistorySeconds(preRollSeconds);
            if (history.Length > 0)
            {
                lock (_bufLock) _buffer.AddRange(history);
            }
        }

        // 订阅 PCM
        _capture.Subscribe(OnPcmChunk);

        // VAD 定时器（200ms 粒度）
        _vadTimer = new System.Timers.Timer(200) { AutoReset = true };
        _vadTimer.Elapsed += (s, e) => CheckVad();
        _vadTimer.Start();

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRecording) return;
        IsRecording = false;

        // 取消订阅
        _capture.Unsubscribe(OnPcmChunk);

        try { _vadTimer?.Stop(); _vadTimer?.Dispose(); } catch { }
        _vadTimer = null;

        byte[] wav;
        lock (_bufLock)
        {
            wav = ToWavBytes(_buffer.ToArray());
            _buffer.Clear();
        }

        RecordingCompleted?.Invoke(this, wav);
        await Task.CompletedTask;
    }

    private void OnPcmChunk(byte[] pcm)
    {
        if (!IsRecording) return;

        lock (_bufLock) _buffer.AddRange(pcm);

        // RMS → 电平
        double rms = ComputeRms(pcm, pcm.Length);
        AudioLevelChanged?.Invoke(this, rms / 32768.0);
        if (rms > SpeechThreshold) _lastSpeech = DateTime.Now;
    }

    private void CheckVad()
    {
        if (!IsRecording) return;
        var now = DateTime.Now;
        var elapsedMs = (now - _lastSpeech).TotalMilliseconds;
        var totalSecs = (now - _startTime).TotalSeconds;

        // 先保证最少录 0.5 秒（避免刚开口就停）
        if (totalSecs < 0.5) return;

        if (elapsedMs > SilenceTimeoutSeconds || totalSecs > MaxRecordingSeconds)
        {
            _ = StopAsync();
        }
    }

    private static double ComputeRms(byte[] buf, int len)
    {
        double sum = 0;
        var samples = len / 2;
        for (int i = 0; i < len; i += 2)
        {
            short s = (short)(buf[i] | (buf[i + 1] << 8));
            sum += s * s;
        }
        return Math.Sqrt(sum / Math.Max(samples, 1));
    }

    private static byte[] ToWavBytes(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);  // PCM
        writer.Write((short)1);  // mono
        writer.Write(ContinuousAudioCapture.SampleRate);
        writer.Write(ContinuousAudioCapture.BytesPerSecond);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (IsRecording) { try { StopAsync().Wait(1000); } catch { } }
    }
}
