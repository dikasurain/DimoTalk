using Vosk;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Vosk 离线唤醒词检测器
/// 持续录音 + 在线 Kaldi 模型识别，匹配关键词
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    private const string ModelDirName = "vosk-model-small-cn-0.22";

    private string _modelPath;
    private Model? _model;
    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    private Func<Task>? _onWake;
    private bool _disposed;
    private static readonly object _extractLock = new();
    private static bool _extracted;

    public string WakeWord { get; set; } = "滴墨";

    /// <param name="modelPath">Vosk 中文声学模型目录路径（如 vosk-model-cn-0.22）</param>
    public VoskWakeWordDetector(string modelPath) => _modelPath = modelPath;

    /// <summary>使用默认模型名（vosk-model-small-cn-0.22），自动解析平台路径</summary>
    public VoskWakeWordDetector() : this(ResolveDefaultModelPath()) { }

    private static string ResolveDefaultModelPath()
    {
        // Windows 桌面：从应用输出目录加载（编译时已 CopyToOutputDirectory）
        if (OperatingSystem.IsWindows())
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, ModelDirName);
            if (Directory.Exists(candidate)) return candidate;
        }

        // Android/iOS：从 MAUI Raw 资源解压到 AppDataDirectory 后加载
        var appData = FileSystem.AppDataDirectory;
        return Path.Combine(appData, ModelDirName);
    }

    /// <summary>
    /// Android/iOS 平台首次启动：从 Raw 资源解压模型文件到 AppDataDirectory
    /// Windows 不需要（直接读输出目录）
    /// </summary>
    public async Task EnsureModelExtractedAsync()
    {
        if (OperatingSystem.IsWindows()) return;
        if (_extracted) return;

        lock (_extractLock)
        {
            if (_extracted) return;

            // 检查目标目录是否已解压过（用 README 文件作为标记）
            var targetDir = _modelPath;
            var marker = Path.Combine(targetDir, "README");
            if (File.Exists(marker) && Directory.Exists(targetDir))
            {
                _extracted = true;
                return;
            }

            Directory.CreateDirectory(targetDir);

            // 列举所有打包的资源文件（按 LogicalName 前缀过滤）
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(ModelDirName) || n.Replace('/', '\\').Contains(ModelDirName))
                .ToList();

            foreach (var resourceName in resourceNames)
            {
                // 把资源名中的 ModelDirName 之后部分作为相对路径
                var normalized = resourceName.Replace('/', '\\');
                var idx = normalized.IndexOf(ModelDirName, StringComparison.Ordinal);
                if (idx < 0) continue;

                var relPath = normalized.Substring(idx + ModelDirName.Length).TrimStart('\\');
                if (string.IsNullOrEmpty(relPath)) continue;

                var destPath = Path.Combine(targetDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fs = File.Create(destPath);
                stream.CopyTo(fs);
            }

            _extracted = true;
        }

        await Task.CompletedTask;
    }

    public async Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct)
    {
        // Android/iOS 首次启动需先解压模型
        await EnsureModelExtractedAsync();

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
    }

    private void ListeningLoop(CancellationToken ct)
    {
#if WINDOWS
        ListeningLoop_Windows(ct);
#else
        ListeningLoop_Android(ct);
#endif
    }

#if WINDOWS
    private void ListeningLoop_Windows(CancellationToken ct)
    {
        using var waveIn = new NAudio.Wave.WaveInEvent
        {
            WaveFormat = new NAudio.Wave.WaveFormat(16000, 1),  // Vosk 要求 16kHz 单声道
            BufferMilliseconds = 200,
        };

        var rec = new VoskRecognizer(_model!, 16000.0f);
        rec.SetMaxAlternatives(0);
        rec.AcceptWaveform(new byte[4096], 0);  // 初始化

        void OnDataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
        {
            if (ct.IsCancellationRequested) { waveIn.StopRecording(); return; }

            if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                var resultJson = rec.Result();
                if (ContainsWakeWord(resultJson) && _onWake != null) _ = _onWake();
            }
            else
            {
                var partial = rec.PartialResult();
                if (ContainsWakeWord(partial) && _onWake != null) _ = _onWake();
            }
        }

        waveIn.DataAvailable += OnDataAvailable;
        waveIn.StartRecording();

        while (!ct.IsCancellationRequested) Thread.Sleep(100);

        try { waveIn.StopRecording(); } catch { }
        waveIn.DataAvailable -= OnDataAvailable;
    }
#else
    private void ListeningLoop_Android(CancellationToken ct)
    {
        // Android 录音待实现：用 Android.AudioRecord（16kHz mono PCM）
        // Vosk 模型已正确解压到 FileSystem.AppDataDirectory/vosk-model-small-cn-0.22/
        // 下一步用 Platform.Android 项目下的 AudioRecordHelper 接入
        throw new PlatformNotSupportedException(
            "Android 平台的 Vosk 录音循环尚未实现。模型已就位，仅缺录音端代码。" +
            "请用 Android.Media.AudioRecord 提供 16kHz mono PCM 数据后调用 VoskRecognizer.AcceptWaveform。");
    }
#endif

    private bool ContainsWakeWord(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        var normalized = json.Replace(" ", "");
        return normalized.Contains(WakeWord);
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
        _model?.Dispose();
        _disposed = true;
    }
}
