using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using OpenAI.Chat;

namespace DimoTalk.Maui.Services;

/// <summary>
/// 自传生成服务 — 从三层记忆中提炼素材，调用 GPT 生成人物自述
/// </summary>
public class AutobiographyService
{
    private readonly MemoryManager _memory;
    private readonly OpenAIClient _ai;

    public AutobiographyService(MemoryManager memory, OpenAIClient ai)
    {
        _memory = memory;
        _ai = ai;
    }

    public async Task<string> GenerateAsync(string userId, string? theme = null, CancellationToken ct = default)
    {
        // 1. 收集素材
        var longTerm = _memory.LongTerm.ListAll(userId, limit: 150);
        var midTerm = _memory.MidTerm.Recall(userId, limit: 20);
        var shortTerm = _memory.ShortTerm.Context
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content)
            .Take(15)
            .ToList();

        if (longTerm.Count == 0 && midTerm.Count == 0 && shortTerm.Count == 0)
        {
            return "记忆匣尚未打开。先和我聊聊天吧，让我慢慢认识你——等积累了足够的故事，自传自会流淌而出。";
        }

        // 2. 组装素材 prompt
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是滴墨讲，一位擅长书写人物自述的 AI 知己。");
        sb.AppendLine("请根据以下素材，为用户撰写一篇自传。要求：");
        sb.AppendLine("- 第一人称「我」叙述，自然流畅，像真人在讲自己的故事");
        sb.AppendLine("- 结构清晰：开篇引入 → 成长经历/兴趣爱好 → 性格特点 → 价值观/人生感悟 → 结尾寄语");
        sb.AppendLine("- 不要把所有素材堆在一起，要有取舍、有叙事节奏、有温度");
        sb.AppendLine("- 字数 600~1000 字，适度文艺但不要浮夸");
        sb.AppendLine("- 素材是 AI 在对话中从用户亲口说过的话里提炼出来的，如素材不全可合理补全但不要编造具体事实");
        sb.AppendLine();
        sb.AppendLine("═══ 长期记忆（关于用户的事实/偏好）═══");
        foreach (var fact in longTerm) sb.AppendLine($"• {fact}");
        sb.AppendLine();
        sb.AppendLine("═══ 中期记忆（近期对话摘要）═══");
        foreach (var sum in midTerm) sb.AppendLine($"• {sum}");
        sb.AppendLine();
        sb.AppendLine("═══ 近期对话（用户原话）═══");
        foreach (var u in shortTerm) sb.AppendLine($"「{u}」");

        if (!string.IsNullOrWhiteSpace(theme))
        {
            sb.AppendLine();
            sb.AppendLine($"本次自传聚焦主题：{theme}");
        }

        // 3. 注入方言约束（跟随用户偏好）
        var dialectKey = Preferences.Default.Get("dialect", DialectRegistry.Mandarin.Key);
        var dialect = DialectRegistry.FindByKey(dialectKey);
        if (dialect.Key != DialectRegistry.Mandarin.Key)
        {
            sb.AppendLine();
            sb.AppendLine("── 硬性风格约束 ──");
            sb.AppendLine(dialect.SystemConstraint);
        }

        // 4. 调用 GPT
        var messages = new List<ChatMessage> { new SystemChatMessage(sb.ToString()) };
        var result = await _ai.ChatAsync(messages, temperature: 0.8);
        return result;
    }

    public async Task<string> ExportMarkdownAsync(string userId, CancellationToken ct = default)
    {
        var auto = await GenerateAsync(userId, ct: ct);
        var meta = $"*生成时间：{DateTime.Now:yyyy年MM月dd日 HH:mm}*";
        return $"# 我的自述\n\n{meta}\n\n---\n\n{auto}";
    }
}
