using Vosk;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Vosk 离线唤醒词检测器 — 订阅 ContinuousAudioCapture 的 PCM 流，不再自建麦克风
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    private const string ModelDirName = "vosk-model-small-cn-0.22";

    private readonly ContinuousAudioCapture _capture;
    private readonly object _voskLock = new();   // 保护 _rec 的 AcceptWaveform / Dispose 互斥
    private string _modelPath;
    private Model? _model;
    private VoskRecognizer? _rec;
    private Func<Task>? _onWake;
    private bool _listening;
    private bool _disposed;

    public string WakeWord { get; set; } = "滴墨";

    /// <summary>唤醒词候选列表（含方言同音字）。运行时改变需重启监听才生效。</summary>
    public IList<string> Aliases { get; } = new List<string>();

    /// <summary>实时 partial result 文本（Vosk 听到的内容）。供调试页订阅。</summary>
    public event EventHandler<string>? PartialResultReceived;

    public VoskWakeWordDetector(ContinuousAudioCapture capture)
        : this(capture, ResolveDefaultModelPath()) { }

    public VoskWakeWordDetector(ContinuousAudioCapture capture, string modelPath)
    {
        _capture = capture;
        _modelPath = modelPath;
    }

    /// <summary>当前生效的识别词表（Aliases 非空用 Aliases，否则仅 WakeWord 本字）</summary>
    private IList<string> EffectiveWords => (Aliases.Count > 0 ? Aliases : new[] { WakeWord });

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

        lock (_voskLock)
        {
            // 三合一改造核心：用 grammar 约束 Vosk 只识别候选词表
            // Vosk 在 grammar 模式下不会输出任意汉字，只输出词表中的词或 [unk]
            // 这把小模型识别不准/同音字干扰问题彻底根治
            var words = EffectiveWords;
            var grammarWords = words.Distinct().Select(w => $"\"{w}\"").ToList();
            // 末尾占位 [unk]：让 Vosk 把非候选词归到未知桶，避免静默噪声误判为某候选
            grammarWords.Add("\"[unk]\"");
            string grammar = "[" + string.Join(", ", grammarWords) + "]";

            _rec = new VoskRecognizer(_model, 16000.0f, grammar);
            _rec.SetMaxAlternatives(0);
            _rec.AcceptWaveform(new byte[4096], 0); // reset
        }

        _onWake = onWakeWordDetected;
        _listening = true;

        // 订阅 ContinuousAudioCapture
        _capture.Subscribe(OnPcmChunk);
        await Task.CompletedTask;
    }

    private void OnPcmChunk(byte[] pcm)
    {
        if (!_listening || _onWake == null) return;

        try
        {
            // 与 StopAsync 的 Dispose 互斥 — 防止 recognizer 被释放后 JNI 访问导致 SIGSEGV
            lock (_voskLock)
            {
                var rec = _rec;
                if (!_listening || rec == null) return;

                if (rec.AcceptWaveform(pcm, pcm.Length))
                {
                    var result = rec.Result();
                    // 调试事件：让设置页能看到 Vosk 听到了什么
                    var resultText = ExtractText(result);
                    if (!string.IsNullOrEmpty(resultText))
                        PartialResultReceived?.Invoke(this, resultText);
                    if (ContainsWakeWord(result))
                    {
                        _listening = false; // 触发后由上层 StopAsync 清理
                        _ = _onWake();
                    }
                }
                else
                {
                    var partial = rec.PartialResult();
                    var partialText = ExtractText(partial);
                    if (!string.IsNullOrEmpty(partialText))
                        PartialResultReceived?.Invoke(this, partialText);
                    if (ContainsWakeWord(partial))
                    {
                        _listening = false;
                        _ = _onWake();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vosk 唤醒检测异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 匹配逻辑：文本中是否包含任一候选词（Aliases 非空）或唤醒词本身。
    /// grammar 模式下 Vosk 只会输出候选词或 [unk]，所以这里是简单的字符串包含。
    /// 但仍保留对 partial 中的"未约束"输出兼容（如用户未配置 Aliases 时回退自由识别）。
    /// </summary>
    private bool ContainsWakeWord(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        var compact = json.Replace(" ", "").Replace("[unk]", "");
        foreach (var w in EffectiveWords)
        {
            if (!string.IsNullOrEmpty(w) && compact.Contains(w)) return true;
        }
        return false;
    }

    /// <summary>从 Vosk JSON 结果中提取 "text" / "partial" 字段的纯文本</summary>
    private static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        // 简易提取：Vosk 输出形如 {"text":"你好"} 或 {"partial":"滴墨"}
        // 避免引入 System.Text.Json 反序列化开销，直接字符串切片
        foreach (var key in new[] { "\"partial\"", "\"text\"" })
        {
            int k = json.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) continue;
            int colon = json.IndexOf(':', k);
            if (colon < 0) continue;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\"')) start++;
            int end = start;
            while (end < json.Length && json[end] != '\"') end++;
            return json.Substring(start, end - start);
        }
        return string.Empty;
    }

    public Task StopAsync()
    {
        _listening = false;
        _capture.Unsubscribe(OnPcmChunk);
        // 与 OnPcmChunk 的 AcceptWaveform 互斥 — 确保 Dispose 时无回调正在使用 recognizer
        lock (_voskLock)
        {
            try { _rec?.Dispose(); } catch { }
            _rec = null;
        }
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
