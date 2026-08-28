using System.Text;
using DimoTalk.Maui.Models;
using OpenAI.Chat;
using DimoTalk.Maui.Services.Memory;

namespace DimoTalk.Maui.Services.AI;

public static class PromptBuilder
{
    public static List<ChatMessage> ToOpenAIMessages(
        string userInput,
        ShortTermMemory shortTerm,
        List<string> midTermSummaries,
        List<MemoryHit> longTermHits,
        string systemPrompt = "你是滴墨讲（DimoTalk），一个温暖贴心的 AI 伙伴。请用自然、友好的方式与用户对话。",
        string? dialectConstraint = null)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(AssembleSystem(systemPrompt, midTermSummaries, longTermHits, dialectConstraint)),
        };

        foreach (var msg in shortTerm.Context)
        {
            messages.Add(msg.Role switch
            {
                MessageRole.User => new UserChatMessage(msg.Content),
                MessageRole.Assistant => new AssistantChatMessage(msg.Content),
                _ => new SystemChatMessage(msg.Content),
            });
        }
        messages.Add(new UserChatMessage(userInput));

        return messages;
    }

    private static string AssembleSystem(string basePrompt, List<string> midTerm, List<MemoryHit> longTerm, string? dialectConstraint)
    {
        var sb = new StringBuilder(basePrompt);

        if (longTerm.Count > 0)
        {
            sb.AppendLine("\n[长期记忆 - 关于用户]");
            foreach (var hit in longTerm) sb.AppendLine($"- {hit.Content}");
        }

        if (midTerm.Count > 0)
        {
            sb.AppendLine("\n[最近对话摘要]");
            sb.AppendLine(string.Join("\n", midTerm));
        }

        // ⚠️ 方言/语气约束放在最后 —— 高优先级，确保不被前面的记忆内容稀释
        if (!string.IsNullOrEmpty(dialectConstraint))
        {
            sb.AppendLine();
            sb.AppendLine("── 硬性输出约束 ──");
            sb.AppendLine(dialectConstraint);
        }

        return sb.ToString();
    }
}
