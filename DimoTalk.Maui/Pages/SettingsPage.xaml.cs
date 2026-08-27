using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services.AI;

namespace DimoTalk.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private UserAiConfig _config = UserAiConfig.Load();

    public SettingsPage()
    {
        InitializeComponent();

        // 服务商下拉
        ProviderPicker.ItemsSource = ProviderRegistry.All;
        var current = ProviderRegistry.FindByKey(_config.ProviderKey) ?? ProviderRegistry.OpenAI;
        ProviderPicker.SelectedItem = current;
        UpdateProviderDescription(current);

        // API Key
        ApiKeyEntry.Text = _config.ApiKey;
        EndpointEntry.Text = _config.EndpointOverride;

        // 模型字段
        ChatModelEntry.Text = _config.ChatModel;
        EmbeddingModelEntry.Text = _config.EmbeddingModel;
        WhisperModelEntry.Text = _config.WhisperModel;
        TtsModelEntry.Text = _config.TtsModel;
        TtsVoicePicker.SelectedItem = _config.TtsVoice;
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

        // 兼容旧字段（让 ChatPage 的 API Key 检查能继续工作）
        Preferences.Set("openai_api_key", _config.ApiKey);

        DisplayAlert("提示", "配置已保存", "确定");
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
    }
}
