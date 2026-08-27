using DimoTalk.Maui.Config;

namespace DimoTalk.Maui.Services.AI;

/// <summary>用户当前 AI 配置（持久化到 Preferences）</summary>
public class UserAiConfig
{
    public string ProviderKey { get; set; } = "openai";
    public string ApiKey { get; set; } = string.Empty;
    public string EndpointOverride { get; set; } = string.Empty;  // 仅 custom/自定义 endpoint
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string WhisperModel { get; set; } = "whisper-1";
    public string TtsModel { get; set; } = "tts-1";
    public string TtsVoice { get; set; } = "alloy";

    /// <summary>取解析后的服务商预设（含 endpoint）</summary>
    public AiProvider ResolveProvider()
    {
        var preset = ProviderRegistry.FindByKey(ProviderKey) ?? ProviderRegistry.OpenAI;
        var endpoint = string.IsNullOrEmpty(EndpointOverride) ? preset.Endpoint : EndpointOverride;
        return preset with { Endpoint = endpoint };
    }

    // --- 持久化 ---
    private const string PKey = "ai_config_";

    public static UserAiConfig Load()
    {
        return new UserAiConfig
        {
            ProviderKey = Preferences.Get(PKey + "provider", "openai"),
            ApiKey = Preferences.Get(PKey + "api_key", string.Empty),
            EndpointOverride = Preferences.Get(PKey + "endpoint", string.Empty),
            ChatModel = Preferences.Get(PKey + "chat_model", "gpt-4o-mini"),
            EmbeddingModel = Preferences.Get(PKey + "emb_model", "text-embedding-3-small"),
            WhisperModel = Preferences.Get(PKey + "whisper_model", "whisper-1"),
            TtsModel = Preferences.Get(PKey + "tts_model", "tts-1"),
            TtsVoice = Preferences.Get(PKey + "tts_voice", "alloy"),
        };
    }

    public void Save()
    {
        Preferences.Set(PKey + "provider", ProviderKey);
        Preferences.Set(PKey + "api_key", ApiKey);
        Preferences.Set(PKey + "endpoint", EndpointOverride);
        Preferences.Set(PKey + "chat_model", ChatModel);
        Preferences.Set(PKey + "emb_model", EmbeddingModel);
        Preferences.Set(PKey + "whisper_model", WhisperModel);
        Preferences.Set(PKey + "tts_model", TtsModel);
        Preferences.Set(PKey + "tts_voice", TtsVoice);
    }

    /// <summary>当用户输入模型名时，自动填充对应预设</summary>
    public void AutoFillFromChatModel(string chatModel)
    {
        ChatModel = chatModel;
        var guess = ProviderRegistry.GuessByModelName(chatModel);
        if (guess == null) return;

        // 只在用户尚未自定义 ProviderKey 时才覆盖
        if (ProviderKey == "openai" || ProviderKey == "custom")
        {
            ProviderKey = guess.Key;
            if (!string.IsNullOrEmpty(guess.DefaultEmbeddingModel))
                EmbeddingModel = guess.DefaultEmbeddingModel;
            if (!string.IsNullOrEmpty(guess.DefaultWhisperModel))
                WhisperModel = guess.DefaultWhisperModel;
            if (!string.IsNullOrEmpty(guess.DefaultTtsModel))
                TtsModel = guess.DefaultTtsModel;
            if (!string.IsNullOrEmpty(guess.DefaultTtsVoice))
                TtsVoice = guess.DefaultTtsVoice;
        }
    }
}
