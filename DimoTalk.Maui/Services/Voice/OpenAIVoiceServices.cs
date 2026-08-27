using OpenAI.Audio;
using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services.AI;

namespace DimoTalk.Maui.Services.Voice;

/// <summary>
/// OpenAI Whisper 语音识别实现
/// 注意：Whisper 仅 OpenAI 官方支持，国产厂商配置后此服务会自动失效
/// </summary>
public class WhisperAsrService : IAsrService
{
    public async Task<string> RecognizeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        var config = UserAiConfig.Load();
        var provider = config.ResolveProvider();
        if (!provider.SupportsWhisper)
        {
            throw new InvalidOperationException(
                $"当前服务商 {provider.Name} 不支持 Whisper ASR。请在设置中切换到 OpenAI 或配置自定义 Whisper 端点。");
        }

        var client = AiClientFactory.CreateAudioClient(config, forTts: false);
        using var stream = new MemoryStream(wavBytes);
        var transcription = await client.TranscribeAudioAsync(stream, "recording.wav");
        return transcription.Value.Text;
    }
}

/// <summary>
/// OpenAI TTS 文本转语音实现
/// 注意：TTS 仅 OpenAI 官方支持
/// </summary>
public class OpenAITtsService : ITtsService
{
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        var config = UserAiConfig.Load();
        var provider = config.ResolveProvider();
        if (!provider.SupportsTts)
        {
            throw new InvalidOperationException(
                $"当前服务商 {provider.Name} 不支持 TTS。请在设置中切换到 OpenAI 或配置自定义 TTS 端点。");
        }

        var client = AiClientFactory.CreateAudioClient(config, forTts: true);
        var speech = await client.GenerateSpeechAsync(text, config.TtsVoice);
        return speech.Value.ToArray();
    }
}
