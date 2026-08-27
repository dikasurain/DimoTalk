namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 基于 NAudio 的音频播放器（Windows 桌面用）
/// Android 端需用平台原生 AudioTrack 或 Plugin.Maui.Audio
/// </summary>
public class AudioPlayer : IAudioPlayer, IDisposable
{
#if WINDOWS
    private NAudio.Wave.WaveOutEvent? _waveOut;
#endif
    private bool _disposed;

    public Task PlayAsync(byte[] audioBytes, CancellationToken ct = default)
    {
#if WINDOWS
        return Task.Run(() =>
        {
            using var ms = new MemoryStream(audioBytes);
            using var reader = new NAudio.Wave.Mp3FileReader(ms);
            _waveOut = new NAudio.Wave.WaveOutEvent();
            _waveOut.Init(reader);

            var tcs = new TaskCompletionSource<bool>();
            _waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

            ct.Register(() => _waveOut?.Stop());
            _waveOut.Play();
            tcs.Task.Wait();
        }, ct);
#else
        throw new PlatformNotSupportedException(
            "Android 平台音频播放待实现。需用 Android.Media.AudioTrack 或 Plugin.Maui.Audio。");
#endif
    }

    public Task PlayPromptAsync(byte[] promptBytes) => PlayAsync(promptBytes);

    public void Dispose()
    {
        if (_disposed) return;
#if WINDOWS
        _waveOut?.Dispose();
#endif
        _disposed = true;
    }
}
