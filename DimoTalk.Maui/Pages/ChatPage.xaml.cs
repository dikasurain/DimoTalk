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

    // 对话模式：闲聊（记忆+方言）/ 快答（单轮直答）
    private bool _isQuickMode;

    public ChatPage(ChatService? chatService, VoiceConversationManager? voiceManager, Func<string> getApiKey)
    {
        InitializeComponent();
        _chatService = chatService;
        _voiceManager = voiceManager;
        _getApiKey = getApiKey;
        MessagesView.ItemsSource = _messages;

        // 恢复上次对话模式
        _isQuickMode = Preferences.Get("chat_mode", ChatService.ChatModeCasual) == ChatService.ChatModeQuick;
        ApplyModeUI();

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

        // 会话收尾：中期记忆摘要 + 当日日记（fire-and-forget，失败静默）
        _ = FinalizeSessionSafeAsync();
    }

    private async Task FinalizeSessionSafeAsync()
    {
        try
        {
            if (_chatService == null) return;
            var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
            Preferences.Set("user_id", userId);
            await _chatService.FinalizeSessionAsync($"conv_{DateTime.Now:yyyyMMdd}", userId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"会话收尾失败: {ex.Message}");
        }
    }

    private bool _historyLoaded;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_sealBreathHandle == null)
            _sealBreathHandle = InkAnimations.SealBreathing(SealBadge);

        // 首次进入：从数据库恢复历史聊天记录
        if (!_historyLoaded)
        {
            _historyLoaded = true;
            LoadChatHistory();
        }

        // 切 Tab 回来时滚到底部最新消息
        if (_messages.Count > 0)
            MessagesView.ScrollTo(_messages[^1], position: ScrollToPosition.End, animate: false);
    }

    /// <summary>从 chat_messages 表恢复最近 60 条聊天记录（app 重启后保留对话）</summary>
    private void LoadChatHistory()
    {
        if (_chatService == null || _messages.Count > 0) return;
        try
        {
            var userId = Preferences.Get("user_id", "");
            Preferences.Set("user_id", userId);
            if (string.IsNullOrEmpty(userId)) return;

            var history = _chatService.LoadHistory(userId, 60);
            foreach (var m in history)
            {
                bool isUser = m.Role == "user";
                DateTime ts = DateTime.TryParse(m.Time, out var t) ? t : DateTime.Now;
                _messages.Add(new ChatBubble { Content = m.Content, IsUser = isUser, Timestamp = ts });
            }
            if (_messages.Count > 0)
                System.Diagnostics.Debug.WriteLine($"已恢复 {history.Count} 条历史消息");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"恢复聊天记录失败: {ex.Message}");
        }
    }

    private void OnEntryFocused(object? sender, FocusEventArgs e)
    {
        // 键盘弹起 → 消息滚到底，确保最后一条不被键盘盖住
        if (_messages.Count > 0)
            MessagesView.ScrollTo(_messages[^1], position: ScrollToPosition.End, animate: true);
    }

    private void OnEntryUnfocused(object? sender, FocusEventArgs e) { }

    private void OnModeClicked(object? sender, EventArgs e)
    {
        // 从闲聊切到快答时，若语音进行中先停止
        if (!_isQuickMode && _voiceManager is { State: not VoiceState.Idle })
            _ = StopVoiceIfRunningAsync();

        _isQuickMode = !_isQuickMode;
        Preferences.Set("chat_mode", _isQuickMode ? ChatService.ChatModeQuick : ChatService.ChatModeCasual);
        ApplyModeUI();
    }

    private async Task StopVoiceIfRunningAsync()
    {
        if (_voiceManager == null || _voiceManager.State == VoiceState.Idle) return;
        try
        {
            await _voiceManager.StopAsync();
            VoiceButton.Text = "语音";
            VoiceButton.BackgroundColor = (Color)Application.Current!.Resources["InkWash"];
            VoiceButton.TextColor = (Color)Application.Current!.Resources["InkMedium"];
        }
        catch { /* 停止失败不阻塞模式切换 */ }
    }

    /// <summary>按当前模式刷新切换按钮文案与状态栏提示</summary>
    private void ApplyModeUI()
    {
        ModeButton.Text = _isQuickMode ? "快答" : "闲聊";
        StatusLabel.Text = _isQuickMode ? "快问快答 · 直击要点" : "墨已研好 · 静候落笔";
        // 快答模式强调速答 → 淡青底；闲聊模式 → 淡墨底
        ModeButton.BackgroundColor = _isQuickMode
            ? (Color)Application.Current!.Resources["OnlineGreen"]
            : (Color)Application.Current!.Resources["InkWash"];
        ModeButton.TextColor = _isQuickMode ? Colors.White : (Color)Application.Current!.Resources["InkMedium"];

        // 语音对话仅闲聊模式可用
        VoiceButton.IsEnabled = !_isQuickMode;
        VoiceButton.Opacity = _isQuickMode ? 0.4 : 1.0;
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
            ApplyModeUI();
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
    /// <summary>AI 长回复（超150字）加宽气泡：右侧留白 60→20，提升长文阅读体验</summary>
    public bool IsLongContent => !IsUser && !IsThinking && Content.Length > 150;
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
