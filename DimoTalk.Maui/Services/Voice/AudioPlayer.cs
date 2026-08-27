using NAudio.Wave;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 基于 NAudio 的音频播放器（Windows 桌面用）
/// Android 端需用平台原生 AudioTrack 或 Plugin.Maui.Audio
/// </summary>
public class AudioPlayer : IAudioPlayer, IDisposable
{
    private WaveOutEvent? _waveOut;
    private bool _disposed;

    public Task PlayAsync(byte[] audioBytes, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = new MemoryStream(audioBytes);
            using var reader = new Mp3FileReader(ms);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(reader);

            var tcs = new TaskCompletionSource<bool>();
            _waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

            ct.Register(() => _waveOut?.Stop());
            _waveOut.Play();
            tcs.Task.Wait();
        }, ct);
    }

    public Task PlayPromptAsync(byte[] promptBytes)
        => PlayAsync(promptBytes);

    public void Dispose()
    {
        if (_disposed) return;
        _waveOut?.Dispose();
        _disposed = true;
    }
}
