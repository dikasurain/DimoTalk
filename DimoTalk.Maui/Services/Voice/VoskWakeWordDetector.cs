using Vosk;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Vosk 离线唤醒词检测器 — 订阅 ContinuousAudioCapture 的 PCM 流，不再自建麦克风
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    private const string ModelDirName = "vosk-model-small-cn-0.22";

    private readonly ContinuousAudioCapture _capture;
    private string _modelPath;
    private Model? _model;
    private VoskRecognizer? _rec;
    private Func<Task>? _onWake;
    private bool _listening;
    private bool _disposed;

    public string WakeWord { get; set; } = "滴墨";

    public VoskWakeWordDetector(ContinuousAudioCapture capture)
        : this(capture, ResolveDefaultModelPath()) { }

    public VoskWakeWordDetector(ContinuousAudioCapture capture, string modelPath)
    {
        _capture = capture;
        _modelPath = modelPath;
    }

    private static string ResolveDefaultModelPath()
    {
        var candidate = Path.Combine(FileSystem.AppDataDirectory, ModelDirName);
        if (Directory.Exists(candidate)) return candidate;
#if WINDOWS
        var dev = Path.Combine(AppContext.BaseDirectory, ModelDirName);
        if (Directory.Exists(dev)) return dev;
#endif
        return candidate;
    }

    /// <summary>Android 首次启动从 APK 资源解压 Vosk 模型</summary>
    public async Task EnsureModelExtractedAsync()
    {
#if WINDOWS
        await Task.CompletedTask;
        return;
#else
        var destDir = Path.Combine(FileSystem.AppDataDirectory, ModelDirName);
        if (Directory.Exists(destDir) && Directory.GetFileSystemEntries(destDir).Length > 0)
        {
            _modelPath = destDir;
            return;
        }
        Directory.CreateDirectory(destDir);

        // 尝试从 APK assets 解压
        try
        {
            var expected = new[]
            {
                "am/final.mdl", "am/tree",
                "conf/mfcc.conf", "conf/model.conf",
                "graph/HCLG.fst", "graph/HCLR.fst", "graph/HLG.fst", "graph/HLR.fst", "graph/LG.fst", "graph/RLG.fst", "graph/words.txt",
                "ivector/final.dubm", "ivector/final.ie", "ivector/final.mda", "ivector/final.ubm",
                "ivector/global_cmvn.stats", "ivector/online_cmvn.conf",
            };
            foreach (var rel in expected)
            {
                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync($"{ModelDirName}/{rel}");
                    var dest = Path.Combine(destDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var fs = File.Create(dest);
                    await stream.CopyToAsync(fs);
                }
                catch { /* 单文件缺失跳过 */ }
            }
        }
        catch { }

        _modelPath = destDir;
        await Task.CompletedTask;
#endif
    }

    public async Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct = default)
    {
        if (_listening) return;

        if (_model == null)
        {
            if (!Directory.Exists(_modelPath))
                throw new DirectoryNotFoundException($"Vosk 模型目录不存在: {_modelPath}");

            _model = new Model(_modelPath);
            Vosk.Vosk.SetLogLevel(-1);
        }

        _rec = new VoskRecognizer(_model, 16000.0f);
        _rec.SetMaxAlternatives(0);
        _rec.AcceptWaveform(new byte[4096], 0); // reset

        _onWake = onWakeWordDetected;
        _listening = true;

        // 订阅 ContinuousAudioCapture
        _capture.Subscribe(OnPcmChunk);
        await Task.CompletedTask;
    }

    private void OnPcmChunk(byte[] pcm)
    {
        if (!_listening || _rec == null || _onWake == null) return;

        try
        {
            if (_rec.AcceptWaveform(pcm, pcm.Length))
            {
                var result = _rec.Result();
                if (ContainsWakeWord(result))
                {
                    _ = _onWake();
                    _listening = false; // 触发后由上层 StopAsync 清理
                }
            }
            else
            {
                var partial = _rec.PartialResult();
                if (ContainsWakeWord(partial))
                {
                    _ = _onWake();
                    _listening = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vosk 唤醒检测异常: {ex.Message}");
        }
    }

    private bool ContainsWakeWord(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        return json.Replace(" ", "").Contains(WakeWord);
    }

    public Task StopAsync()
    {
        if (!_listening) return Task.CompletedTask;
        _listening = false;
        _capture.Unsubscribe(OnPcmChunk);
        try { _rec?.Dispose(); } catch { }
        _rec = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (!_disposed)
        {
            _model?.Dispose();
            _disposed = true;
        }
    }
}
