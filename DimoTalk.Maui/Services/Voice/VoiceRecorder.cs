using NAudio.Wave;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// 录音服务：持续录音直到静音 2 秒或超过 30 秒
/// 返回 16kHz 单声道 PCM（适合 Whisper）
/// </summary>
public class VoiceRecorder
{
    private readonly WaveInEvent _waveIn = new()
    {
        WaveFormat = new WaveFormat(16000, 1),
        BufferMilliseconds = 100,
    };
    private readonly List<byte> _buffer = new();
    private DateTime _lastSpeech = DateTime.MinValue;
    private bool _stopped;

    public event EventHandler<byte[]>? RecordingCompleted;

    public void StartRecording()
    {
        _buffer.Clear();
        _lastSpeech = DateTime.Now;
        _stopped = false;

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_stopped) return;

        _buffer.AddRange(e.Buffer.Take(e.BytesRecorded));
        var now = DateTime.Now;

        // 检测是否有语音（简单能量阈值）
        if (HasSpeech(e.Buffer, e.BytesRecorded)) _lastSpeech = now;

        if ((now - _lastSpeech).TotalSeconds > 2 || _buffer.Count > 16000 * 2 * 30)
        {
            Stop();
        }
    }

    private static bool HasSpeech(byte[] buffer, int length)
    {
        // 简单能量检测：RMS 阈值
        double sum = 0;
        for (int i = 0; i < length; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sum += sample * sample;
        }
        var rms = Math.Sqrt(sum / (length / 2));
        return rms > 500;  // 经验阈值
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;

        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;

        // 转为 WAV 文件字节数组（PCM + WAV 头）
        var wavBytes = ToWavBytes(_buffer.ToArray());
        RecordingCompleted?.Invoke(this, wavBytes);
        _buffer.Clear();
    }

    /// <summary>
    /// 裸 PCM 转 WAV 字节数组（含 RIFF 头）
    /// </summary>
    private static byte[] ToWavBytes(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);  // PCM
        writer.Write((short)1);  // mono
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return ms.ToArray();
    }
}
