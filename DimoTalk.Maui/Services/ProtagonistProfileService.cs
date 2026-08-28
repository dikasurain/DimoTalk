using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using OpenAI.Chat;

namespace DimoTalk.Maui.Services;

/// <summary>主人公技能/特征标签</summary>
public record SkillTag(string Name, string Evidence);

/// <summary>主人公画像：GPT 从记忆中自动判定</summary>
public class ProtagonistProfile
{
    public string Summary { get; set; } = "";
    public List<SkillTag> Skills { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 主人公档案服务 —— 从三层记忆自动判定用户的技能/职业/爱好画像
/// 判定结果存 SQLite，供自传分章与 Soul 回复参考
/// </summary>
public class ProtagonistProfileService
{
    private readonly MemoryManager _memory;
    private readonly OpenAIClient _ai;
    private readonly AutobiographyStore _store;

    public ProtagonistProfileService(MemoryManager memory, OpenAIClient ai)
    {
        _memory = memory;
        _ai = ai;
        _store = memory.Autobiography;
    }

    /// <summary>读取已保存画像（无则返回 null）</summary>
    public ProtagonistProfile? Get(string userId) => _store.LoadProfile(userId);

    /// <summary>
    /// 从记忆中判定主人公画像（技能/职业/爱好），存库并返回
    /// </summary>
    public async Task<ProtagonistProfile> AnalyzeAsync(string userId, CancellationToken ct = default)
    {
        var material = CollectMaterial(userId);
        var profile = await AnalyzeCoreAsync(material, ct);
        _store.SaveProfile(userId, profile);
        return profile;
    }

    /// <summary>取画像，库中无则现算</summary>
    public async Task<ProtagonistProfile> GetOrAnalyzeAsync(string userId, CancellationToken ct = default)
        => Get(userId) ?? await AnalyzeAsync(userId, ct);

    /// <summary>删除画像（记忆清空时）</summary>
    public void Invalidate(string userId) => _store.DeleteProfile(userId);

    private string CollectMaterial(string userId)
    {
        var longTerm = _memory.LongTerm.ListAll(userId, limit: 150);
        var midTerm = _memory.MidTerm.Recall(userId, limit: 20);
        var shortTerm = _memory.ShortTerm.Context
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content)
            .Take(20)
            .ToList();

        var sb = new System.Text.StringBuilder();
        foreach (var f in longTerm) sb.AppendLine($"• {f}");
        foreach (var s in midTerm) sb.AppendLine($"• {s}");
        foreach (var u in shortTerm) sb.AppendLine($"「{u}」");
        return sb.ToString();
    }

    private async Task<ProtagonistProfile> AnalyzeCoreAsync(string material, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(material))
            return new ProtagonistProfile { Summary = "素材尚少，多聊聊才能看清轮廓。" };

        var sys =
            "你是档案分析师。根据素材判定主人公（用户）的画像。\n" +
            "输出严格 JSON（不要 markdown 代码块）：" +
            "{\"summary\":\"30字内的一句话画像\",\"skills\":[{\"name\":\"技能/职业/爱好\",\"evidence\":\"依据的原话或事实\"}]}\n" +
            "规则：\n" +
            "1. skills 是技能、职业、长期爱好、显著性格特质，3~8 个，按显著度排序。\n" +
            "2. evidence 必须来自素材，找不到依据就不要写。\n" +
            "3. 素材太少就输出少，宁缺毋滥，禁止编造。";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(sys),
            new UserChatMessage(material),
        };
        var raw = await _ai.ChatAsync(messages, temperature: 0.2);
        return ParseProfile(raw);
    }

    internal static ProtagonistProfile ParseProfile(string raw)
    {
        try
        {
            // 剥掉可能的 markdown 代码块
            var json = raw.Trim();
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) throw new FormatException();
            json = json.Substring(start, end - start + 1);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var profile = new ProtagonistProfile();
            if (doc.RootElement.TryGetProperty("summary", out var sum))
                profile.Summary = sum.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("skills", out var skills) && skills.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var s in skills.EnumerateArray())
                {
                    var name = s.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var ev = s.TryGetProperty("evidence", out var e) ? e.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name))
                        profile.Skills.Add(new SkillTag(name!, ev ?? ""));
                }
            }
            return profile;
        }
        catch
        {
            return new ProtagonistProfile { Summary = "画像解析失败，请重新判定。" };
        }
    }
}
