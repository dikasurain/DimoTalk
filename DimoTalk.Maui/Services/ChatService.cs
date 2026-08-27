using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;

namespace DimoTalk.Maui.Services;

public class ChatService
{
    private readonly MemoryManager _memoryManager;
    private readonly OpenAIClient _ai;

    public ChatService(MemoryManager memoryManager, OpenAIClient ai)
    {
        _memoryManager = memoryManager;
        _ai = ai;
    }

    public async Task<string> SendMessageAsync(string userId, string userInput)
    {
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

        var messages = PromptBuilder.ToOpenAIMessages(userInput, _memoryManager.ShortTerm, midSummaries, ltmHits);
        var reply = await _ai.ChatAsync(messages);

        var assistantMsg = new Message { Role = MessageRole.Assistant, Content = reply, Timestamp = DateTime.Now };
        _memoryManager.AddToShortTerm(assistantMsg);

        _ = Task.Run(() => TryExtractToLongTermAsync(userId, userInput));

        return reply;
    }

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
    }
}
