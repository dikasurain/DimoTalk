using NAudio.Wave;
using Vosk;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// Vosk 离线唤醒词检测器
/// 持续录音 + 在线 Kaldi 模型识别，匹配关键词
/// </summary>
public class VoskWakeWordDetector : IWakeWordDetector
{
    private readonly string _modelPath;
    private Model? _model;
    private WaveInEvent? _waveIn;
    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    private Func<Task>? _onWake;
    private bool _disposed;

    public string WakeWord { get; set; } = "滴墨";

    /// <param name="modelPath">Vosk 中文声学模型目录路径（如 vosk-model-cn-0.22）</param>
    public VoskWakeWordDetector(string modelPath) => _modelPath = modelPath;

    public Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct)
    {
        if (_model == null)
        {
            _model = new Model(_modelPath);
            Vosk.Vosk.SetLogLevel(-1);
        }

        _onWake = onWakeWordDetected;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _loopTask = Task.Run(() => ListeningLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private void ListeningLoop(CancellationToken ct)
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 1),  // Vosk 要求 16kHz 单声道
            BufferMilliseconds = 200,
        };

        var rec = new VoskRecognizer(_model!, 16000.0f);
        rec.SetMaxAlternatives(0);
        rec.AcceptWaveform(new byte[4096], 0);  // 初始化

        void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (ct.IsCancellationRequested) { _waveIn!.StopRecording(); return; }

            if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                var resultJson = rec.Result();
                if (ContainsWakeWord(resultJson) && _onWake != null)
                {
                    _ = _onWake();
                }
            }
            else
            {
                // Partial result，用于快速响应
                var partial = rec.PartialResult();
                if (ContainsWakeWord(partial) && _onWake != null)
                {
                    _ = _onWake();
                }
            }
        }

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();

        // 阻塞直到取消
        while (!ct.IsCancellationRequested) Thread.Sleep(100);

        try { _waveIn.StopRecording(); } catch { }
        _waveIn.DataAvailable -= OnDataAvailable;
    }

    private bool ContainsWakeWord(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        // Vosk 中文模型识别结果用空格分隔词，关键词去空格匹配
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
        _waveIn?.Dispose();
        _model?.Dispose();
        _disposed = true;
    }
}
