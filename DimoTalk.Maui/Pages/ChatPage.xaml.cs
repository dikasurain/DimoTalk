using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.Voice;

namespace DimoTalk.Maui.Pages;

public partial class ChatPage : ContentPage
{
    private readonly ChatService? _chatService;
    private readonly VoiceConversationManager? _voiceManager;
    private readonly Func<string> _getApiKey;
    private readonly List<ChatBubble> _messages = new();

    public ChatPage(ChatService? chatService, VoiceConversationManager? voiceManager, Func<string> getApiKey)
    {
        InitializeComponent();
        _chatService = chatService;
        _voiceManager = voiceManager;
        _getApiKey = getApiKey;
        MessagesView.ItemsSource = _messages;
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var chatService = _chatService;
        if (chatService == null) return;

        var text = InputEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var apiKey = _getApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            await DisplayAlert("提示", "请先在设置中配置 API Key", "确定");
            return;
        }

        _messages.Add(new ChatBubble { Content = text, IsUser = true });
        MessagesView.ItemsSource = null;
        MessagesView.ItemsSource = _messages;
        InputEntry.Text = string.Empty;
        SendButton.IsEnabled = false;
        SendButton.Text = "发送中...";

        try
        {
            var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
            Preferences.Set("user_id", userId);

            var reply = await chatService.SendMessageAsync(userId, text);
            _messages.Add(new ChatBubble { Content = reply, IsUser = false });
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", ex.Message, "确定");
        }
        finally
        {
            SendButton.IsEnabled = true;
            SendButton.Text = "发送";
            MessagesView.ItemsSource = null;
            MessagesView.ItemsSource = _messages;
        }
    }

    private async void OnVoiceClicked(object? sender, EventArgs e)
    {
        if (_voiceManager == null) return;

        var apiKey = _getApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            await DisplayAlert("提示", "请先在设置中配置 API Key", "确定");
            return;
        }

        if (_voiceManager.State == VoiceState.Idle)
        {
            var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
            Preferences.Set("user_id", userId);
            await _voiceManager.StartAsync(userId);
            VoiceButton.Text = "停止";
            VoiceButton.BackgroundColor = Color.FromArgb("F44336");
        }
        else
        {
            await _voiceManager.StopAsync();
            VoiceButton.Text = "语音";
            VoiceButton.BackgroundColor = Color.FromArgb("FF9800");
        }
    }
}

public class ChatBubble
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public Color BubbleColor => IsUser ? Color.FromArgb("E91E63") : Color.FromArgb("EEEEEE");
    public Color TextColor => IsUser ? Colors.White : Colors.Black;
    public LayoutOptions Alignment => IsUser ? LayoutOptions.End : LayoutOptions.Start;
}
