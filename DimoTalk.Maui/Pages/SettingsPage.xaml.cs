using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services.AI;

namespace DimoTalk.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private UserAiConfig _config = UserAiConfig.Load();
    private DialectInfo? _currentDialect;
    private const string ScrollPosKey = "settings_scroll_y";

    public SettingsPage()
    {
        InitializeComponent();

        // 服务商下拉
        ProviderPicker.ItemsSource = ProviderRegistry.All;
        var current = ProviderRegistry.FindByKey(_config.ProviderKey) ?? ProviderRegistry.OpenAI;
        ProviderPicker.SelectedItem = current;
        UpdateProviderDescription(current);

        // 方言/风格下拉
        DialectPicker.ItemsSource = DialectRegistry.All.ToList();
        var dialectKey = Preferences.Get("dialect", DialectRegistry.Mandarin.Key);
        _currentDialect = DialectRegistry.FindByKey(dialectKey);
        DialectPicker.SelectedItem = _currentDialect;
        DialectDescLabel.Text = _currentDialect.Description;
        DialectPicker.SelectedIndexChanged += (_, _) =>
        {
            if (DialectPicker.SelectedItem is DialectInfo d)
            {
                _currentDialect = d;
                DialectDescLabel.Text = d.Description;
            }
        };

        // API Key
        ApiKeyEntry.Text = _config.ApiKey;
        EndpointEntry.Text = _config.EndpointOverride;

        // 模型字段
        ChatModelEntry.Text = _config.ChatModel;
        EmbeddingModelEntry.Text = _config.EmbeddingModel;
        WhisperModelEntry.Text = _config.WhisperModel;
        TtsModelEntry.Text = _config.TtsModel;
        TtsVoicePicker.SelectedItem = _config.TtsVoice;

        // 语音设置
        WakeWordEntry.Text = Preferences.Get("wake_word", "滴墨");
        VoiceWakeSwitch.IsToggled = Preferences.Get("voice_wake_enabled", false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 恢复上次滚动位置
        var y = Preferences.Get(ScrollPosKey, 0.0);
        if (y > 0)
            _ = RootScrollView.ScrollToAsync(0, y, false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 保存滚动位置
        Preferences.Set(ScrollPosKey, RootScrollView.ScrollY);
    }

    private void OnProviderChanged(object? sender, EventArgs e)
    {
        if (ProviderPicker.SelectedItem is not AiProvider provider) return;
        _config.ProviderKey = provider.Key;

        UpdateProviderDescription(provider);

        // 仅 custom 显示 Endpoint 输入
        var isCustom = provider.Key == "custom";
        EndpointLabel.IsVisible = isCustom;
        EndpointBorder.IsVisible = isCustom;

        // 切换服务商时，用对应默认值填充模型字段（用户仍可编辑覆盖）
        if (!isCustom)
        {
            if (!string.IsNullOrEmpty(provider.DefaultChatModel) && string.IsNullOrEmpty(ChatModelEntry.Text))
                ChatModelEntry.Text = provider.DefaultChatModel;
            if (!string.IsNullOrEmpty(provider.DefaultEmbeddingModel))
                EmbeddingModelEntry.Text = provider.DefaultEmbeddingModel;
            if (!string.IsNullOrEmpty(provider.DefaultWhisperModel))
                WhisperModelEntry.Text = provider.DefaultWhisperModel;
            if (!string.IsNullOrEmpty(provider.DefaultTtsModel))
                TtsModelEntry.Text = provider.DefaultTtsModel;
            if (!string.IsNullOrEmpty(provider.DefaultTtsVoice))
                TtsVoicePicker.SelectedItem = provider.DefaultTtsVoice;
        }
    }

    private void OnChatModelChanged(object? sender, TextChangedEventArgs e)
    {
        // 输入模型名时自动路由到对应服务商
        var text = e.NewTextValue?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var guess = ProviderRegistry.GuessByModelName(text);
        if (guess == null) return;

        // 静默更新 Provider（不触发 OnProviderChanged 的默认填充）
        _config.ProviderKey = guess.Key;
        var idx = Array.FindIndex(ProviderRegistry.All, p => p.Key == guess.Key);
        if (idx >= 0) ProviderPicker.SelectedIndex = idx;
        UpdateProviderDescription(guess);
    }

    private void UpdateProviderDescription(AiProvider provider)
    {
        ProviderDescription.Text = provider.Description;
        WebsiteLink.Text = provider.Website;
        WebsiteLink.IsVisible = !string.IsNullOrEmpty(provider.Website);
    }

    private async void OnWebsiteTapped(object? sender, EventArgs e)
    {
        var url = WebsiteLink.Text;
        if (string.IsNullOrEmpty(url)) return;
        try { await Browser.OpenAsync(url, BrowserLaunchMode.External); }
        catch { /* 平台不支持 */ }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        _config.ApiKey = ApiKeyEntry.Text?.Trim() ?? string.Empty;
        _config.EndpointOverride = EndpointEntry.Text?.Trim() ?? string.Empty;
        _config.ChatModel = ChatModelEntry.Text?.Trim() ?? _config.ChatModel;
        _config.EmbeddingModel = EmbeddingModelEntry.Text?.Trim() ?? _config.EmbeddingModel;
        _config.WhisperModel = WhisperModelEntry.Text?.Trim() ?? _config.WhisperModel;
        _config.TtsModel = TtsModelEntry.Text?.Trim() ?? _config.TtsModel;
        _config.TtsVoice = TtsVoicePicker.SelectedItem as string ?? "alloy";

        _config.Save();

        // 兼容旧字段
        Preferences.Set("openai_api_key", _config.ApiKey);

        // 语音设置
        var wakeWord = WakeWordEntry.Text?.Trim();
        Preferences.Set("wake_word", string.IsNullOrEmpty(wakeWord) ? "滴墨" : wakeWord);
        Preferences.Set("voice_wake_enabled", VoiceWakeSwitch.IsToggled);

        // 方言/风格
        if (DialectPicker.SelectedItem is DialectInfo d)
            Preferences.Set("dialect", d.Key);

        DisplayAlert("提示", "配置已保存", "确定");
    }

    private async void OnGenerateAutoBiographyClicked(object? sender, EventArgs e)
    {
        var apiKey = Preferences.Get("openai_api_key", string.Empty);
        if (string.IsNullOrEmpty(apiKey))
        {
            await DisplayAlert("提示", "请先在上方配置 API Key", "确定");
            return;
        }

        var userId = Preferences.Get("user_id", Guid.NewGuid().ToString());
        Preferences.Set("user_id", userId);

        try
        {
            var ai = Handler?.MauiContext?.Services.GetService<DimoTalk.Maui.Services.AutobiographyService>();
            if (ai == null)
            {
                await DisplayAlert("提示", "服务未就绪，请重启应用", "确定");
                return;
            }

            // 显示 Loading 弹窗
            var loadingPage = new ContentPage
            {
                Content = new VerticalStackLayout
                {
                    Padding = 30,
                    Spacing = 16,
                    Children =
                    {
                        new Label { Text = "研墨润笔中…", FontSize = 16, HorizontalOptions = LayoutOptions.Center },
                        new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center },
                    }
                }
            };
            await Navigation.PushModalAsync(loadingPage);

            var text = await ai.GenerateAsync(userId);

            await Navigation.PopModalAsync();

            // 显示结果
            await DisplayAlert("📜 我的自述", text, "好的");
        }
        catch (Exception ex)
        {
            await DisplayAlert("生成失败", ex.Message, "确定");
        }
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _config = new UserAiConfig();
        ProviderPicker.SelectedItem = ProviderRegistry.OpenAI;
        ApiKeyEntry.Text = string.Empty;
        EndpointEntry.Text = string.Empty;
        ChatModelEntry.Text = _config.ChatModel;
        EmbeddingModelEntry.Text = _config.EmbeddingModel;
        WhisperModelEntry.Text = _config.WhisperModel;
        TtsModelEntry.Text = _config.TtsModel;
        TtsVoicePicker.SelectedItem = _config.TtsVoice;
        WakeWordEntry.Text = "滴墨";
        VoiceWakeSwitch.IsToggled = false;
        DialectPicker.SelectedItem = DialectRegistry.Mandarin;

        Preferences.Set("wake_word", "滴墨");
        Preferences.Set("voice_wake_enabled", false);
        Preferences.Set("dialect", DialectRegistry.Mandarin.Key);
    }
}
