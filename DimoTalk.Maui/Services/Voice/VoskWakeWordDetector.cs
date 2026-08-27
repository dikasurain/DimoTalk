#if WINDOWS
using NAudio.Wave;
using Vosk;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Vosk 离线唤醒词检测器（Windows 桌面实现）
/// 持续录音 + 在线 Kaldi 模型识别，匹配关键词
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    private const string ModelDirName = "vosk-model-small-cn-0.22";

    private string _modelPath;
    private Model? _model;
    private WaveInEvent? _waveIn;
    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    private Func<Task>? _onWake;
    private bool _disposed;
    private static readonly object _extractLock = new();
    private static bool _extracted;

    public string WakeWord { get; set; } = "滴墨";

    public VoskWakeWordDetector(string modelPath) => _modelPath = modelPath;

    public VoskWakeWordDetector() : this(ResolveDefaultModelPath()) { }

    private static string ResolveDefaultModelPath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, ModelDirName);
        if (Directory.Exists(candidate)) return candidate;

        var appData = FileSystem.AppDataDirectory;
        return Path.Combine(appData, ModelDirName);
    }

    public Task EnsureModelExtractedAsync() => Task.CompletedTask;  // Windows 直接从输出目录加载

    public async Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct)
    {
        if (_model == null)
        {
            if (!Directory.Exists(_modelPath))
                throw new DirectoryNotFoundException($"Vosk 模型目录不存在: {_modelPath}");

            _model = new Model(_modelPath);
            Vosk.Vosk.SetLogLevel(-1);
        }

        _onWake = onWakeWordDetected;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => ListeningLoop(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    private void ListeningLoop(CancellationToken ct)
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 1),
            BufferMilliseconds = 200,
        };

        var rec = new VoskRecognizer(_model!, 16000.0f);
        rec.SetMaxAlternatives(0);
        rec.AcceptWaveform(new byte[4096], 0);

        void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (ct.IsCancellationRequested) { _waveIn!.StopRecording(); return; }

            if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                if (ContainsWakeWord(rec.Result()) && _onWake != null) _ = _onWake();
            }
            else
            {
                if (ContainsWakeWord(rec.PartialResult()) && _onWake != null) _ = _onWake();
            }
        }

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();

        while (!ct.IsCancellationRequested) Thread.Sleep(100);

        try { _waveIn.StopRecording(); } catch { }
        _waveIn.DataAvailable -= OnDataAvailable;
    }

    private bool ContainsWakeWord(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        return json.Replace(" ", "").Contains(WakeWord);
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_loopTask != null) await _loopTask;
            _cts.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _waveIn?.Dispose();
        _model?.Dispose();
        _disposed = true;
    }
}

#else
namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Android/iOS 唤醒词检测器占位实现
/// Vosk native lib 暂未集成，使用 NoOp 占位避免 DI 容器失败
/// 后续在 Platforms/Android 下接 Android.Media.AudioRecord + VoskRecognizer 实现
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    public string WakeWord { get; set; } = "滴墨";

    public Task EnsureModelExtractedAsync() => Task.CompletedTask;

    public Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct) =>
        Task.FromException(new PlatformNotSupportedException(
            "Android 唤醒词检测待实现。需在 Platforms/Android 接入 Vosk native 库 + AudioRecord。"));

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
#endif
