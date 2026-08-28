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

    /// <summary>Android 首次启动从 APK 资源解压 Vosk 模型（遍历目录全部解压，不硬编码文件列表）</summary>
    public async Task EnsureModelExtractedAsync()
    {
#if WINDOWS
        await Task.CompletedTask;
        return;
#else
        var destDir = Path.Combine(FileSystem.AppDataDirectory, ModelDirName);

        // 验证解压完整性 — 检查关键文件是否存在
        string[] requiredFiles = new[]
        {
            "am/final.mdl",
            "ivector/final.mat",   // ← 缺这个会 SIGSEGV
            "graph/HCLr.fst",
            "conf/model.conf",
        };
        if (Directory.Exists(destDir) && requiredFiles.All(f => File.Exists(Path.Combine(destDir, f))))
        {
            _modelPath = destDir;
            return;
        }

        // 之前解压不完整 — 删掉重来
        try { Directory.Delete(destDir, true); } catch { }
        Directory.CreateDirectory(destDir);

        // 遍历 APK assets 解压整个模型目录
        try
        {
            using var assets = Android.App.Application.Context!.Assets!;
            var rootFiles = assets.List(ModelDirName);
            if (rootFiles != null)
            {
                await ExtractAssetDirAsync(assets, ModelDirName, destDir);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vosk 模型解压失败: {ex.Message}");
        }

        _modelPath = destDir;
        await Task.CompletedTask;

        static async Task ExtractAssetDirAsync(Android.Content.Res.AssetManager assets, string assetPath, string destPath)
        {
            // Assets.List 返回当前目录下的文件和子目录名
            var entries = assets.List(assetPath);
            if (entries == null || entries.Length == 0) return;

            foreach (var entry in entries)
            {
                var fullAssetPath = $"{assetPath}/{entry}";
                var fullDestPath = Path.Combine(destPath, entry);

                // 检查是目录还是文件：尝试 Open，如果能打开就是文件，否则是目录
                try
                {
                    using var stream = assets.Open(fullAssetPath);
                    // 能打开 → 文件
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath)!);
                    using var fs = File.Create(fullDestPath);
                    await stream.CopyToAsync(fs);
                }
                catch (Java.IO.FileNotFoundException)
                {
                    // 目录 → 递归
                    Directory.CreateDirectory(fullDestPath);
                    await ExtractAssetDirAsync(assets, fullAssetPath, fullDestPath);
                }
            }
        }
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
