using System.Text;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;
using DimoTalk.Maui.Config;

namespace DimoTalk.Maui.Services.AI;

public class OpenAIClient
{
    private readonly ChatClient _chatClient;
    private readonly EmbeddingClient _embeddingClient;

    public OpenAIClient(string apiKey)
    {
        var credential = new ApiKeyCredential(apiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri("https://api.openai.com/v1") };
        _chatClient = new ChatClient(AppConfig.DefaultModel, credential, options);
        _embeddingClient = new EmbeddingClient(AppConfig.DefaultEmbeddingModel, credential, options);
    }

    public async Task<string> ChatAsync(List<ChatMessage> messages, double temperature = 0.7)
    {
        var options = new ChatCompletionOptions { Temperature = (float)temperature };
        var completion = await _chatClient.CompleteChatAsync(messages, options);
        return completion.Value.Content[0].Text;
    }

    public async Task<float[]> EmbedAsync(string input)
    {
        var embedding = await _embeddingClient.GenerateEmbeddingAsync(input);
        return embedding.Value.ToFloats().ToArray();
    }

    public async Task<string> SummarizeConversationAsync(List<string> userMessages, List<string> assistantReplies)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < userMessages.Count; i++)
        {
            sb.AppendLine($"用户: {userMessages[i]}");
            if (i < assistantReplies.Count) sb.AppendLine($"AI: {assistantReplies[i]}");
        }

        var prompt = $"""
            请将以下对话压缩为 200-500 字的摘要，保留：
            1. 对话主题和关键进展
            2. 用户表达的偏好、身份、事实
            3. 未解决的问题

            对话内容:
            {sb}
        """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("你是一个精准的对话摘要助手。"),
            new UserChatMessage(prompt),
        };

        return await ChatAsync(messages, temperature: 0.3);
    }

    public async Task<bool> ShouldExtractToLongTermAsync(string message)
    {
        var keywords = new[] { "我叫", "我是", "我喜欢", "我讨厌", "我住在", "我来自", "我在", "我有", "我的", "我想", "我希望", "我以后" };
        if (keywords.Any(k => message.Contains(k))) return true;

        var result = await ChatAsync(new List<ChatMessage>
        {
            new SystemChatMessage("判断用户消息是否包含值得长期记忆的信息（偏好、身份、事实、计划等）。只回答\"是\"或\"否\"。"),
            new UserChatMessage(message),
        }, temperature: 0);

        return result.Trim().Contains("是");
    }

    public async Task<string?> ExtractKeyFactAsync(string message)
    {
        var result = await ChatAsync(new List<ChatMessage>
        {
            new SystemChatMessage("从用户消息中提取一条值得长期记忆的关键事实，用简洁的陈述句表达。如果没有则回答\"无\"。"),
            new UserChatMessage(message),
        }, temperature: 0);

        var text = result.Trim();
        return text == "无" ? null : text;
    }
}
