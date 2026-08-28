using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using OpenAI.Chat;
using System.Text.Json;

namespace DimoTalk.Maui.Services;

/// <summary>章节大纲条目</summary>
public record OutlineItem(int Index, string Title, string Summary);

/// <summary>
/// 自传生成服务（章节版）：
/// 1. 收集三层记忆素材
/// 2. 主人公技能画像判定（ProtagonistProfileService）
/// 3. GPT 生成章节大纲（3~6 章）
/// 4. 逐章生成正文（携带画像 + 前文摘要保持连贯）
/// 5. 存 SQLite，可导出 docx
/// </summary>
public class AutobiographyService
{
    private readonly MemoryManager _memory;
    private readonly OpenAIClient _ai;
    private readonly ProtagonistProfileService _profileService;

    /// <summary>逐章生成进度回调（index 1-based，章标题）</summary>
    public event Action<int, int, string>? ChapterProgress;

    public AutobiographyService(MemoryManager memory, OpenAIClient ai)
    {
        _memory = memory;
        _ai = ai;
        _profileService = new ProtagonistProfileService(memory, ai);
    }

    // ── 素材收集 ──

    public string CollectMaterial(string userId)
    {
        var longTerm = _memory.LongTerm.ListAll(userId, limit: 150);
        var midTerm = _memory.MidTerm.Recall(userId, limit: 20);
        var shortTerm = _memory.ShortTerm.Context
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content)
            .Take(15)
            .ToList();

        var sb = new System.Text.StringBuilder();
        if (longTerm.Count > 0)
        {
            sb.AppendLine("═══ 长期记忆（用户亲口说过的事实/偏好）═══");
            foreach (var fact in longTerm) sb.AppendLine($"• {fact}");
        }
        if (midTerm.Count > 0)
        {
            sb.AppendLine("═══ 中期记忆（近期对话摘要）═══");
            foreach (var s in midTerm) sb.AppendLine($"• {s}");
        }
        if (shortTerm.Count > 0)
        {
            sb.AppendLine("═══ 近期对话（用户原话）═══");
            foreach (var u in shortTerm) sb.AppendLine($"「{u}」");
        }
        return sb.ToString();
    }

    // ── 完整生成流程 ──

    public async Task<List<ChapterInfo>> GenerateBookAsync(string userId, CancellationToken ct = default)
    {
        var material = CollectMaterial(userId);
        if (string.IsNullOrWhiteSpace(material))
            throw new InvalidOperationException("记忆匣尚未打开。先和我聊聊天，积累些故事再来吧。");

        // 1. 主人公画像（判定 + 存库）
        var profile = await _profileService.AnalyzeAsync(userId, ct);

        // 2. 章节大纲
        var outline = await GenerateOutlineAsync(material, profile, ct);

        // 3. 逐章生成
        var chapters = new List<(int, string, string)>();
        string previousDigest = "";
        for (int i = 0; i < outline.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = outline[i];
            var content = await GenerateChapterAsync(material, profile, outline, i, previousDigest, ct);
            chapters.Add((item.Index, item.Title, content));
            previousDigest = content.Length > 300 ? content[..300] + "…" : content;
            ChapterProgress?.Invoke(i + 1, outline.Count, item.Title);
        }

        // 4. 存库
        _memory.Autobiography.SaveChapters(userId, chapters);
        return _memory.Autobiography.LoadChapters(userId);
    }

    // ── 大纲 ──

    private async Task<List<OutlineItem>> GenerateOutlineAsync(string material, ProtagonistProfile profile, CancellationToken ct)
    {
        var sys =
            "你是自传结构师。根据素材与主人公画像，设计一份第一人称自传的章节大纲。\n" +
            "输出严格 JSON 数组（不要 markdown 代码块）：\n" +
            "[{\"title\":\"章标题（4~10字）\",\"summary\":\"本章要写什么（40字内）\"}]\n" +
            "规则：\n" +
            "1. 3~6 章，有起承转合：引入/来历 → 技能与热爱 → 性格与关系 → 价值观 → 结尾寄语（不必全有，按素材取舍）。\n" +
            "2. 技能画像中的每一项都要在某章有落点。\n" +
            "3. 只依据素材，不编造具体事件。";

        var user = new System.Text.StringBuilder();
        user.AppendLine($"【主人公画像】{profile.Summary}");
        foreach (var s in profile.Skills) user.AppendLine($"  - {s.Name}（依据：{s.Evidence}）");
        user.AppendLine();
        user.Append(material);

        var raw = await _ai.ChatAsync(new List<ChatMessage>
        {
            new SystemChatMessage(sys),
            new UserChatMessage(user.ToString()),
        }, temperature: 0.6);

        return ParseOutline(raw);
    }

    internal static List<OutlineItem> ParseOutline(string raw)
    {
        try
        {
            var json = raw.Trim();
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start < 0 || end <= start) throw new FormatException();
            json = json.Substring(start, end - start + 1);

            var items = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? new();
            var result = new List<OutlineItem>();
            int idx = 1;
            foreach (var it in items)
            {
                var title = it.TryGetProperty("title", out var t) ? t.GetString() : null;
                var summary = it.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(title))
                    result.Add(new OutlineItem(idx++, title!, summary));
            }
            if (result.Count == 0) throw new FormatException();
            return result;
        }
        catch
        {
            // 大纲解析失败 → 兜底单章
            return new List<OutlineItem> { new(1, "我的故事", "讲述我自己") };
        }
    }

    // ── 章节 ──

    private async Task<string> GenerateChapterAsync(
        string material, ProtagonistProfile profile, List<OutlineItem> outline, int chapterIdx, string previousDigest, CancellationToken ct)
    {
        var chapter = outline[chapterIdx];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是滴墨讲，一位擅长书写人物自述的 AI 知己。请撰写第一人称自传中的一章。");
        sb.AppendLine();
        sb.AppendLine("【写作要求】");
        sb.AppendLine("- 第一人称「我」叙述，像真人在讲自己的故事，自然流畅有温度");
        sb.AppendLine("- 素材是 AI 从用户亲口说过的话里提炼的，可合理补全氛围但不要编造具体事实");
        sb.AppendLine("- 字数 600~1000 字，适度文艺但不浮夸");
        sb.AppendLine("- 只写本章，不要越界写其他章节的内容；结尾留有余味");
        if (chapterIdx > 0)
            sb.AppendLine($"- 上一章结尾（保持连贯，不要重复）：\n  「{previousDigest}」");

        sb.AppendLine();
        sb.AppendLine($"【全书章节】{string.Join(" / ", outline.Select(o => o.Title))}");
        sb.AppendLine($"【本章】第{chapter.Index}章 {chapter.Title} —— {chapter.Summary}");

        sb.AppendLine();
        sb.AppendLine($"【主人公画像】{profile.Summary}");
        foreach (var s in profile.Skills) sb.AppendLine($"  - {s.Name}（依据：{s.Evidence}）");

        sb.AppendLine();
        sb.AppendLine("【素材】");
        sb.Append(material);

        // 方言约束跟随用户偏好
        var dialectKey = Preferences.Default.Get("dialect", DialectRegistry.Mandarin.Key);
        var dialect = DialectRegistry.FindByKey(dialectKey);
        if (dialect.Key != DialectRegistry.Mandarin.Key)
        {
            sb.AppendLine();
            sb.AppendLine("── 硬性风格约束 ──");
            sb.AppendLine(dialect.SystemConstraint);
        }

        return await _ai.ChatAsync(new List<ChatMessage> { new SystemChatMessage(sb.ToString()) }, temperature: 0.8);
    }
}
