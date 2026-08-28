namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 音频播放器：跨平台实现
/// Windows → NAudio.WaveOutEvent
/// Android → Android.Media.MediaPlayer
/// 支持 MP3（TTS 输出）和 WAV（本地提示音）
/// </summary>
public class AudioPlayer : IAudioPlayer, IDisposable
{
    private bool _disposed;

#if WINDOWS
    private NAudio.Wave.WaveOutEvent? _waveOut;
#else
    private Android.Media.MediaPlayer? _mediaPlayer;
#endif

    public async Task PlayAsync(byte[] audioBytes, CancellationToken ct = default)
    {
#if WINDOWS
        await Task.Run(() =>
        {
            using var ms = new MemoryStream(audioBytes);
            NAudio.Wave.IWaveReader reader;
            try { reader = new NAudio.Wave.Mp3FileReader(ms); }
            catch { ms.Position = 0; reader = new NAudio.Wave.AudioFileReader(ms); }

            _waveOut = new NAudio.Wave.WaveOutEvent();
            _waveOut.Init(reader);

            var tcs = new TaskCompletionSource<bool>();
            _waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

            ct.Register(() => _waveOut?.Stop());
            _waveOut.Play();
            tcs.Task.Wait();
        }, ct);
#else
        await Task.Run(() =>
        {
            try
            {
                try { _mediaPlayer?.Stop(); _mediaPlayer?.Release(); } catch { }

                // MediaPlayer 需要文件路径，写临时文件
                var ext = audioBytes.Length > 4 && audioBytes[0] == 'R' && audioBytes[1] == 'I' ? ".wav" : ".mp3";
                var tempFile = Path.Combine(FileSystem.CacheDirectory, $"dimotalk_play_{Guid.NewGuid():N}{ext}");
                File.WriteAllBytes(tempFile, audioBytes);

                _mediaPlayer = new Android.Media.MediaPlayer();
                _mediaPlayer.SetDataSource(tempFile);
                _mediaPlayer.Prepare();

                var tcs = new TaskCompletionSource<bool>();
                _mediaPlayer.Completion += (s, e) => { tcs.TrySetResult(true); try { File.Delete(tempFile); } catch { } };

                ct.Register(() =>
                {
                    try { _mediaPlayer?.Stop(); } catch { }
                    tcs.TrySetCanceled();
                });

                _mediaPlayer.Start();
                tcs.Task.Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"播放失败: {ex.Message}");
            }
        }, ct);
#endif
    }

    public Task PlayPromptAsync(byte[] promptBytes) => PlayAsync(promptBytes);

    public void Dispose()
    {
        if (_disposed) return;
#if WINDOWS
        _waveOut?.Dispose();
#else
        try { _mediaPlayer?.Release(); } catch { }
#endif
        _disposed = true;
    }
}
