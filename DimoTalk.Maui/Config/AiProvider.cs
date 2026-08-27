namespace DimoTalk.Maui.Config;

/// <summary>单个 AI 服务商预设</summary>
public record AiProvider
{
    public string Key { get; init; } = string.Empty;        // "openai" / "deepseek" / ...
    public string Name { get; init; } = string.Empty;       // 显示名
    public string Endpoint { get; init; } = string.Empty;  // OpenAI 兼容 endpoint
    public string DefaultChatModel { get; init; } = string.Empty;
    public string DefaultEmbeddingModel { get; init; } = string.Empty;
    public string DefaultWhisperModel { get; init; } = "whisper-1";
    public string DefaultTtsModel { get; init; } = "tts-1";
    public string DefaultTtsVoice { get; init; } = "alloy";
    public bool SupportsWhisper { get; init; } = true;      // 国产厂商一般不支持
    public bool SupportsTts { get; init; } = true;
    public string Website { get; init; } = string.Empty;   // 申请 API Key 的网址
    public string Description { get; init; } = string.Empty;
}

/// <summary>服务商预设表 + 模型名路由</summary>
public static class ProviderRegistry
{
    public static readonly AiProvider OpenAI = new()
    {
        Key = "openai",
        Name = "OpenAI 官方",
        Endpoint = "https://api.openai.com/v1",
        DefaultChatModel = "gpt-4o-mini",
        DefaultEmbeddingModel = "text-embedding-3-small",
        Website = "https://platform.openai.com/api-keys",
        Description = "GPT 系列，支持 Whisper 语音识别和 TTS"
    };

    public static readonly AiProvider DeepSeek = new()
    {
        Key = "deepseek",
        Name = "DeepSeek 深度求索",
        Endpoint = "https://api.deepseek.com/v1",
        DefaultChatModel = "deepseek-chat",
        DefaultEmbeddingModel = "deepseek-chat",  // 暂无独立 embedding 模型
        SupportsWhisper = false,
        SupportsTts = false,
        Website = "https://platform.deepseek.com/api_keys",
        Description = "国内高性价比，DeepSeek-V3 性能强"
    };

    public static readonly AiProvider Moonshot = new()
    {
        Key = "moonshot",
        Name = "Moonshot 月之暗面",
        Endpoint = "https://api.moonshot.cn/v1",
        DefaultChatModel = "moonshot-v1-8k",
        DefaultEmbeddingModel = "embedding-2",
        SupportsWhisper = false,
        SupportsTts = false,
        Website = "https://platform.moonshot.cn",
        Description = "Kimi 系列，长上下文窗口"
    };

    public static readonly AiProvider Zhipu = new()
    {
        Key = "zhipu",
        Name = "Zhipu 智谱",
        Endpoint = "https://open.bigmodel.cn/api/paas/v4",
        DefaultChatModel = "glm-4-flash",
        DefaultEmbeddingModel = "embedding-3",
        SupportsWhisper = false,
        SupportsTts = false,
        Website = "https://open.bigmodel.cn/usercenter/apikeys",
        Description = "GLM 系列，国产大模型"
    };

    public static readonly AiProvider Ollama = new()
    {
        Key = "ollama",
        Name = "Ollama 本地",
        Endpoint = "http://localhost:11434/v1",
        DefaultChatModel = "llama3.2",
        DefaultEmbeddingModel = "nomic-embed-text",
        SupportsWhisper = false,
        SupportsTts = false,
        Website = "https://ollama.com",
        Description = "本地部署，无需 API Key"
    };

    public static readonly AiProvider Custom = new()
    {
        Key = "custom",
        Name = "自定义 OpenAI 兼容",
        Endpoint = "",
        DefaultChatModel = "",
        DefaultEmbeddingModel = "",
        SupportsWhisper = false,
        SupportsTts = false,
        Website = "",
        Description = "任何 OpenAI 兼容 API（自建、代理、其他厂商）"
    };

    /// <summary>所有预设服务商（用于设置页下拉）</summary>
    public static readonly AiProvider[] All =
    {
        OpenAI, DeepSeek, Moonshot, Zhipu, Ollama, Custom
    };

    /// <summary>按 Key 查找预设</summary>
    public static AiProvider? FindByKey(string? key)
        => All.FirstOrDefault(p => p.Key == key);

    /// <summary>按模型名启发式路由（仅用于"输入模型名自动选服务商"场景）</summary>
    public static AiProvider? GuessByModelName(string modelName)
    {
        var lower = modelName.ToLowerInvariant();
        if (lower.StartsWith("gpt") || lower.StartsWith("text-embedding") ||
            lower.StartsWith("whisper") || lower.StartsWith("tts")) return OpenAI;
        if (lower.StartsWith("deepseek")) return DeepSeek;
        if (lower.StartsWith("moonshot") || lower.Contains("kimi")) return Moonshot;
        if (lower.StartsWith("glm")) return Zhipu;
        if (lower.StartsWith("llama") || lower.StartsWith("qwen") ||
            lower.StartsWith("nomic")) return Ollama;
        return null;
    }
}
