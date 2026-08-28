using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;

namespace DimoTalk.Maui.Services;

public class ChatService
{
    public const string ChatModeCasual = "chat";
    public const string ChatModeQuick = "quick";

    private readonly MemoryManager _memoryManager;
    private readonly OpenAIClient _ai;
    private readonly AutobiographyService? _autobiography;

    public ChatService(MemoryManager memoryManager, OpenAIClient ai, AutobiographyService? autobiography = null)
    {
        _memoryManager = memoryManager;
        _ai = ai;
        _autobiography = autobiography;
    }

    public async Task<string> SendMessageAsync(string userId, string userInput, bool forceCasual = false)
    {
        // ── 快问快答模式：零记忆、零方言、单轮直答（不污染闲聊上下文） ──
        // forceCasual：语音对话始终走闲聊链路，不受模式开关影响
        var mode = Preferences.Default.Get("chat_mode", ChatModeCasual);
        if (!forceCasual && mode == ChatModeQuick)
        {
            return await _ai.ChatAsync(PromptBuilder.ToQuickMessages(userInput));
        }

        // ── 闲聊模式：完整记忆链路 ──
        var userMsg = new Message { Role = MessageRole.User, Content = userInput, Timestamp = DateTime.Now };
        _memoryManager.AddToShortTerm(userMsg);

        var ltmTask = _ai.EmbedAsync(userInput)
            .ContinueWith(async t =>
            {
                if (t.IsFaulted) return new List<MemoryHit>();
                return _memoryManager.LongTerm.Recall(userId, t.Result);
            }).Unwrap();

        var midTask = Task.Run(() => _memoryManager.MidTerm.Recall(userId));

        await Task.WhenAll(ltmTask, midTask);
        var ltmHits = await ltmTask;
        var midSummaries = await midTask;

        // 读取方言/语气偏好 → 注入 system prompt 末尾的硬性约束
        var dialectKey = Preferences.Default.Get("dialect", DialectRegistry.Mandarin.Key);
        var dialect = DialectRegistry.FindByKey(dialectKey);
        var dialectConstraint = dialect.SystemConstraint;

        var messages = PromptBuilder.ToOpenAIMessages(
            userInput, _memoryManager.ShortTerm, midSummaries, ltmHits,
            systemPrompt: SoulRegistry.Current().ToPrompt(),
            dialectConstraint: dialectConstraint);
        var reply = await _ai.ChatAsync(messages);

        var assistantMsg = new Message { Role = MessageRole.Assistant, Content = reply, Timestamp = DateTime.Now };
        _memoryManager.AddToShortTerm(assistantMsg);

        // 聊天记录全量落库（user + assistant）
        try
        {
            _memoryManager.Autobiography.SaveMessage(userId, "user", userInput);
            _memoryManager.Autobiography.SaveMessage(userId, "assistant", reply);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"聊天记录落库失败: {ex.Message}");
        }

        _ = Task.Run(() => TryExtractToLongTermAsync(userId, userInput));

        return reply;
    }

    /// <summary>恢复最近 limit 条聊天记录（正序）</summary>
    public List<ChatMessageRow> LoadHistory(string userId, int limit = 100)
        => _memoryManager.Autobiography.LoadMessages(userId, limit);

    private async Task TryExtractToLongTermAsync(string userId, string userInput)
    {
        try
        {
            if (!await _ai.ShouldExtractToLongTermAsync(userInput)) return;
            var fact = await _ai.ExtractKeyFactAsync(userInput);
            if (string.IsNullOrEmpty(fact)) return;

            var embedding = await _ai.EmbedAsync(fact);
            _memoryManager.LongTerm.Store(userId, fact, embedding);
            System.Diagnostics.Debug.WriteLine($"长期记忆已写入: {fact}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"长期记忆提取失败: {ex.Message}");
        }
    }

    public async Task FinalizeSessionAsync(string conversationId, string userId)
    {
        var shortTerm = _memoryManager.ShortTerm.Context;
        if (shortTerm.Count == 0) return;

        var userMsgs = shortTerm.Where(m => m.Role == MessageRole.User).Select(m => m.Content).ToList();
        var assistantMsgs = shortTerm.Where(m => m.Role == MessageRole.Assistant).Select(m => m.Content).ToList();

        var summary = await _ai.SummarizeConversationAsync(userMsgs, assistantMsgs);
        await _memoryManager.OnSessionEndAsync(conversationId, userId, summary);

        // 自动生成/合并当日日记（fire-and-forget，失败静默不打扰用户）
        if (_autobiography != null && userMsgs.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _autobiography.GenerateDiaryAsync(userId, userMsgs, assistantMsgs);
                    System.Diagnostics.Debug.WriteLine("当日日记已生成/合并");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"日记生成失败: {ex.Message}");
                }
            });
        }
    }
}
