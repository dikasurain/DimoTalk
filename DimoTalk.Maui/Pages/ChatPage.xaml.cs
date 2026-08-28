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

        // 记忆系统就绪状态提示（MauiProgram.InitializeMemoryAsync 是异步的，延迟检查）
        _ = Task.Delay(2500).ContinueWith(_ =>
        {
            if (MauiProgram.MemoryInstance == null)
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusLabel.Text = "初始化中 · 记忆系统加载…");
        }, TaskScheduler.Default);
    }

    private void RefreshList(bool scrollToEnd = true)
    {
        MessagesView.ItemsSource = null;
        MessagesView.ItemsSource = _messages;
        if (scrollToEnd && _messages.Count > 0)
            MessagesView.ScrollTo(_messages[^1], position: ScrollToPosition.End, animate: false);
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
        RefreshList();
        InputEntry.Text = string.Empty;
        SendButton.IsEnabled = false;

        // "正在思考"提示气泡
        var thinking = new ChatBubble { Content = "正在思考…", IsThinking = true };
        _messages.Add(thinking);
        RefreshList();

        try
        {
            var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
            Preferences.Set("user_id", userId);

            var reply = await chatService.SendMessageAsync(userId, text);
            _messages.Remove(thinking);
            _messages.Add(new ChatBubble { Content = reply, IsUser = false });
        }
        catch (Exception ex)
        {
            _messages.Remove(thinking);
            _messages.Add(new ChatBubble { Content = $"出错了：{ex.Message}", IsUser = false, IsError = true });
        }
        finally
        {
            SendButton.IsEnabled = true;
            RefreshList();
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
            try
            {
                var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
                Preferences.Set("user_id", userId);
                await _voiceManager.StartAsync(userId);
                VoiceButton.Text = "停止";
                VoiceButton.BackgroundColor = (Color)Application.Current!.Resources["AccentRed"];
                VoiceButton.TextColor = Colors.White;
                StatusLabel.Text = "聆听中 · 说出「滴墨」唤醒";
            }
            catch (Exception ex)
            {
                await DisplayAlert("语音启动失败", ex.Message, "确定");
            }
        }
        else
        {
            await _voiceManager.StopAsync();
            VoiceButton.Text = "语音";
            VoiceButton.BackgroundColor = (Color)Application.Current!.Resources["PrimaryLight"];
            VoiceButton.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
            StatusLabel.Text = "在线 · 记忆系统已就绪";
        }
    }
}

public class ChatBubble
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public bool IsThinking { get; set; }
    public bool IsError { get; set; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Time => Timestamp.ToString("HH:mm");

    public Color BubbleColor => IsError
        ? (Color)Application.Current!.Resources["AccentRed"]
        : IsUser
            ? (Color)Application.Current!.Resources["Primary"]
            : (Color)Application.Current!.Resources["BubbleAI"];

    public Color TextColor => IsError
        ? Colors.White
        : IsUser ? Colors.White : (Color)Application.Current!.Resources["TextPrimary"];

    public Color TimeColor => IsUser || IsError
        ? Color.FromArgb("#B8B8F0")
        : (Color)Application.Current!.Resources["TextSecondary"];

    public LayoutOptions Alignment => IsUser ? LayoutOptions.End : LayoutOptions.Start;
}
