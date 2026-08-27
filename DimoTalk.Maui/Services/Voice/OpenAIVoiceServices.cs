using System.ClientModel;
using System.Text;
using OpenAI;
using OpenAI.Audio;
using DimoTalk.Maui.Config;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// OpenAI Whisper 语音识别实现
/// </summary>
public class WhisperAsrService : IAsrService
{
    private readonly AudioClient _audioClient;

    public WhisperAsrService(string apiKey)
    {
        var credential = new ApiKeyCredential(apiKey);
        _audioClient = new AudioClient(AppConfig.DefaultWhisperModel, credential);
    }

    public async Task<string> RecognizeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(wavBytes);
        var transcription = await _audioClient.TranscribeAudioAsync(stream, "recording.wav");
        return transcription.Value.Text;
    }
}

/// <summary>
/// OpenAI TTS 文本转语音实现
/// </summary>
public class OpenAITtsService : ITtsService
{
    private readonly AudioClient _audioClient;
    private readonly string _voice;

    public OpenAITtsService(string apiKey, string voice = "alloy")
    {
        var credential = new ApiKeyCredential(apiKey);
        _audioClient = new AudioClient(AppConfig.DefaultTtsModel, credential);
        _voice = voice;
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        var speech = await _audioClient.GenerateSpeechAsync(text, _voice);
        return speech.Value.ToArray();
    }
}
