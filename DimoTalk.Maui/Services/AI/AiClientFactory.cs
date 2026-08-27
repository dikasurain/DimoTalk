using System.ClientModel;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace DimoTalk.Maui.Services.AI;

/// <summary>
/// 根据当前 UserAiConfig 生成 OpenAI 兼容的 client 实例
/// 单次请求构造、单次使用、无需 DI 缓存
/// </summary>
public static class AiClientFactory
{
    public static ChatClient CreateChatClient(UserAiConfig config)
    {
        var provider = config.ResolveProvider();
        var credential = new ApiKeyCredential(string.IsNullOrEmpty(config.ApiKey) ? "ollama" : config.ApiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) };
        return new ChatClient(config.ChatModel, credential, options);
    }

    public static EmbeddingClient CreateEmbeddingClient(UserAiConfig config)
    {
        var provider = config.ResolveProvider();
        var credential = new ApiKeyCredential(string.IsNullOrEmpty(config.ApiKey) ? "ollama" : config.ApiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) };
        return new EmbeddingClient(config.EmbeddingModel, credential, options);
    }

    public static AudioClient CreateAudioClient(UserAiConfig config, bool forTts)
    {
        var provider = config.ResolveProvider();
        var credential = new ApiKeyCredential(string.IsNullOrEmpty(config.ApiKey) ? "ollama" : config.ApiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) };
        return new AudioClient(forTts ? config.TtsModel : config.WhisperModel, credential, options);
    }
}
