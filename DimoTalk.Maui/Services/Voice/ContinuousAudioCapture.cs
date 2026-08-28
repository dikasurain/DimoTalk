namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 单一持续音频采集源：统一 16kHz mono PCM，订阅者模式 + 环形历史缓冲
/// 平台实现：Windows → NAudio WaveInEvent，Android → AudioRecord
/// </summary>
public class ContinuousAudioCapture : IAsyncDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BytesPerSample = 2; // PCM 16-bit
    public const int BytesPerSecond = SampleRate * Channels * BytesPerSample; // 32000

    /// <summary>历史缓冲秒数（唤醒时可追回这段音频补到录音开头）</summary>
    public const int HistorySeconds = 2;

    private readonly List<Action<byte[]>> _subscribers = new();
    private readonly object _subLock = new();

    // 环形历史缓冲（2 秒 PCM）
    private readonly byte[] _history = new byte[BytesPerSecond * HistorySeconds];
    private int _historyWritePos;

    public event EventHandler<double>? AudioLevelChanged; // RMS 0~1

    public bool IsCapturing { get; private set; }

#if WINDOWS
    private NAudio.Wave.WaveInEvent? _waveIn;
#else
    private Android.Media.AudioRecord? _audioRecord;
    private Thread? _readThread;
    private volatile bool _running;
#endif

    public async Task StartAsync()
    {
        if (IsCapturing) return;
        IsCapturing = true;
        _historyWritePos = 0;

#if WINDOWS
        await StartWindowsAsync();
#else
        await StartAndroidAsync();
#endif
    }

    public async Task StopAsync()
    {
        if (!IsCapturing) return;
        IsCapturing = false;

#if WINDOWS
        try { _waveIn?.StopRecording(); } catch { }
        if (_waveIn != null) { _waveIn.DataAvailable -= OnWindowsData; _waveIn.Dispose(); _waveIn = null; }
#else
        _running = false;
        try { _audioRecord?.Stop(); _audioRecord?.Release(); } catch { }
        _audioRecord = null;
#endif
        await Task.CompletedTask;
    }

    /// <summary>订阅 PCM 分片（每片通常 100~200ms）</summary>
    public void Subscribe(Action<byte[]> subscriber)
    {
        lock (_subLock) _subscribers.Add(subscriber);
    }

    public void Unsubscribe(Action<byte[]> subscriber)
    {
        lock (_subLock) _subscribers.Remove(subscriber);
    }

    /// <summary>获取最近 N 秒的历史音频（N ≤ HistorySeconds），用于唤醒时追回被截断的语音开头</summary>
    public byte[] GetHistorySeconds(int seconds)
    {
        int bytesToCopy = Math.Min(seconds, HistorySeconds) * BytesPerSecond;
        if (bytesToCopy <= 0 || bytesToCopy > _history.Length)
            return Array.Empty<byte>();

        var result = new byte[bytesToCopy];
        lock (_history)
        {
            int start = (_historyWritePos - bytesToCopy + _history.Length) % _history.Length;
            int firstChunk = _history.Length - start;
            if (firstChunk > bytesToCopy) firstChunk = bytesToCopy;
            Buffer.BlockCopy(_history, start, result, 0, firstChunk);
            if (bytesToCopy > firstChunk)
                Buffer.BlockCopy(_history, 0, result, firstChunk, bytesToCopy - firstChunk);
        }
        return result;
    }

    private void DistributeChunk(byte[] chunk, int bytes)
    {
        // 写历史环形缓冲
        lock (_history)
        {
            for (int i = 0; i < bytes; i++)
            {
                _history[_historyWritePos] = chunk[i];
                _historyWritePos = (_historyWritePos + 1) % _history.Length;
            }
        }

        // RMS 电平
        double rms = ComputeRms(chunk, bytes);
        AudioLevelChanged?.Invoke(this, rms / 32768.0);

        // 分发订阅者
        Action<byte[]>[] copy;
        lock (_subLock) { copy = _subscribers.ToArray(); }
        if (copy.Length == 0) return;

        var slice = new byte[bytes];
        Buffer.BlockCopy(chunk, 0, slice, 0, bytes);
        foreach (var sub in copy)
        {
            try { sub(slice); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"订阅者异常: {ex.Message}"); }
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

#if WINDOWS
    private Task StartWindowsAsync()
    {
        _waveIn = new NAudio.Wave.WaveInEvent
        {
            WaveFormat = new NAudio.Wave.WaveFormat(SampleRate, Channels, BytesPerSample * 8),
            BufferMilliseconds = 100,
        };
        _waveIn.DataAvailable += OnWindowsData;
        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    private void OnWindowsData(object? sender, NAudio.Wave.WaveInEventArgs e)
    {
        if (!IsCapturing) return;
        DistributeChunk(e.Buffer, e.BytesRecorded);
    }
#else
    private Task StartAndroidAsync()
    {
        if (!OperatingSystem.IsAndroid()) return Task.CompletedTask;

        int bufSize = Android.Media.AudioRecord.GetMinBufferSize(
            SampleRate, (Android.Media.ChannelIn)1, (Android.Media.Encoding)2);
        if (bufSize <= 0) bufSize = 3200;

        _audioRecord = new Android.Media.AudioRecord(
            (Android.Media.AudioSource)1, SampleRate,
            (Android.Media.ChannelIn)1, (Android.Media.Encoding)2, bufSize);

        // 验证 AudioRecord 初始化成功（权限未授予时 State == Uninitialized）
        if ((int)_audioRecord.State != 1)
        {
            _audioRecord.Release();
            _audioRecord = null;
            IsCapturing = false;
            throw new InvalidOperationException("AudioRecord 初始化失败 — 请确认已授予麦克风权限");
        }

        _audioRecord.StartRecording();
        _running = true;

        _readThread = new Thread(() => AndroidReadLoop()) { IsBackground = true };
        _readThread.Start();
        return Task.CompletedTask;
    }

    private void AndroidReadLoop()
    {
        var readBuf = new byte[4096];
        while (_running && _audioRecord != null)
        {
            var state = _audioRecord.RecordingState;
            if (state != Android.Media.RecordState.Recording) break;

            var read = _audioRecord.Read(readBuf, 0, readBuf.Length);
            if (read > 0)
            {
                try { DistributeChunk(readBuf, read); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"分发异常: {ex.Message}"); }
            }
        }
    }
#endif

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lock (_subLock) _subscribers.Clear();
    }
}
