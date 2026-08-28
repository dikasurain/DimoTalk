using System.Collections.ObjectModel;
using DimoTalk.Maui.Models;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.Voice;

namespace DimoTalk.Maui.Pages;

public partial class ChatPage : ContentPage
{
    private readonly ChatService? _chatService;
    private readonly VoiceConversationManager? _voiceManager;
    private readonly Func<string> _getApiKey;
    private readonly ObservableCollection<ChatBubble> _messages = new();

    // 动画句柄
    private CancellationTokenStartInfo? _sealBreathHandle;
    private CancellationTokenStartInfo? _grindingHandle;

    public ChatPage(ChatService? chatService, VoiceConversationManager? voiceManager, Func<string> getApiKey)
    {
        InitializeComponent();
        _chatService = chatService;
        _voiceManager = voiceManager;
        _getApiKey = getApiKey;
        MessagesView.ItemsSource = _messages;

        // 顶栏印章常驻呼吸动效
        _sealBreathHandle = InkAnimations.SealBreathing(SealBadge);

        // 记忆系统就绪状态提示（MauiProgram.InitializeMemoryAsync 是异步的，延迟检查）
        _ = Task.Delay(2500).ContinueWith(_ =>
        {
            if (MauiProgram.MemoryInstance == null)
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusLabel.Text = "研墨中 · 记忆系统加载…");
        }, TaskScheduler.Default);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 页面离开时停止动画，避免后台空转
        InkAnimations.Stop(_grindingHandle);
        InkAnimations.Stop(_sealBreathHandle);
        _sealBreathHandle = null;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_sealBreathHandle == null)
            _sealBreathHandle = InkAnimations.SealBreathing(SealBadge);

        // 切 Tab 回来时滚到底部最新消息
        if (_messages.Count > 0)
            MessagesView.ScrollTo(_messages[^1], position: ScrollToPosition.End, animate: false);
    }

    private void RefreshList(bool scrollToEnd = true)
    {
        // ObservableCollection 增量更新会自动刷新 ListView
        // 不再 ItemsSource = null → 强制重置（会导致滚动位置回顶）
        if (scrollToEnd && _messages.Count > 0)
            MessagesView.ScrollTo(_messages[^1], position: ScrollToPosition.End, animate: true);
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

        // 落墨动效：按钮印章钤盖 + 墨滴涟漪
        _ = InkAnimations.SealStampAsync(SendButton);
        _ = InkAnimations.InkRippleAsync(SendButton);

        // "研墨构思中"提示气泡（淡墨底 + 斜体，DataTrigger 已在 XAML 中处理样式）
        var thinking = new ChatBubble { Content = "研墨构思中…", IsThinking = true };
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
                // 运行时权限请求（Android 6.0+ 必须动态申请 RECORD_AUDIO）
#if ANDROID
                var status = await Permissions.RequestAsync<Permissions.Microphone>();
                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlert("需要麦克风权限", "请在系统设置中允许滴墨讲使用麦克风", "确定");
                    return;
                }
#endif

                var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
                Preferences.Set("user_id", userId);
                await _voiceManager.StartAsync(userId);
                VoiceButton.Text = "停止";
                VoiceButton.BackgroundColor = (Color)Application.Current!.Resources["Cinnabar"];
                VoiceButton.TextColor = Color.FromArgb("#F5F2EA");
                var wakeWord = Preferences.Get("wake_word", "滴墨");
                StatusLabel.Text = $"侧耳聆听 · 唤之曰「{wakeWord}」";
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
            VoiceButton.BackgroundColor = (Color)Application.Current!.Resources["InkWash"];
            VoiceButton.TextColor = (Color)Application.Current!.Resources["InkMedium"];
            StatusLabel.Text = "墨已研好 · 静候落笔";
        }
    }
}

public class ChatBubble
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public bool IsThinking { get; set; }
    public bool IsError { get; set; }
    /// <summary>思考态只显示墨点动画，隐藏正文与时间</summary>
    public bool ShowContent => !IsThinking;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Time => Timestamp.ToString("HH:mm");

    public Color BubbleColor => IsError
        ? (Color)Application.Current!.Resources["AccentRed"]
        : IsUser
            ? (Color)Application.Current!.Resources["InkMedium"]
            : (Color)Application.Current!.Resources["BubbleAI"];

    public Color TextColor => IsError
        ? Colors.White
        : IsUser ? Color.FromArgb("#F5F2EA") : (Color)Application.Current!.Resources["TextPrimary"];

    public Color TimeColor => IsUser || IsError
        ? Color.FromArgb("#B5AFA0")
        : (Color)Application.Current!.Resources["TextSecondary"];

    public LayoutOptions Alignment => IsUser ? LayoutOptions.End : LayoutOptions.Start;
}
