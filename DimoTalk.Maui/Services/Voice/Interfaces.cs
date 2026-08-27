namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 语音交互状态机
/// </summary>
public enum VoiceState
{
    /// <summary>空闲：不监听，需用户手动启动</summary>
    Idle,
    /// <summary>监听唤醒词中</summary>
    Listening,
    /// <summary>唤醒词已触发，正在录制用户问题</summary>
    Recording,
    /// <summary>正在 ASR + GPT 处理</summary>
    Processing,
    /// <summary>正在 TTS 合成与播放</summary>
    Reply,
}

/// <summary>
/// 唤醒词检测器接口
/// </summary>
public interface IWakeWordDetector : IAsyncDisposable
{
    /// <summary>设置匹配的唤醒词（如"滴墨"、"dimotalk"）</summary>
    string WakeWord { get; set; }

    /// <summary>启动持续监听，回调在每次检测到关键词时触发</summary>
    Task StartAsync(Func<Task> onWakeWordDetected, CancellationToken ct);

    /// <summary>停止监听</summary>
    Task StopAsync();
}

/// <summary>
/// 自动语音识别接口
/// </summary>
public interface IAsrService
{
    /// <summary>将音频流转为文本</summary>
    Task<string> RecognizeAsync(byte[] wavBytes, CancellationToken ct = default);
}

/// <summary>
/// 文本转语音接口
/// </summary>
public interface ITtsService
{
    /// <summary>将文本合成为音频字节（mp3）</summary>
    Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// 音频播放器接口
/// </summary>
public interface IAudioPlayer
{
    Task PlayAsync(byte[] audioBytes, CancellationToken ct = default);
    Task PlayPromptAsync(byte[] promptBytes); // 短提示音（唤醒反馈）
}
