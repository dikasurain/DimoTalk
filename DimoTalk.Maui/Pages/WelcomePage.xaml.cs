using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services.AI;

namespace DimoTalk.Maui.Pages;

public partial class WelcomePage : ContentPage
{
    private UserAiConfig _config = UserAiConfig.Load();

    public WelcomePage()
    {
        InitializeComponent();

        // 服务商下拉
        ProviderPicker.ItemsSource = ProviderRegistry.All;
        var current = ProviderRegistry.FindByKey(_config.ProviderKey) ?? ProviderRegistry.OpenAI;
        ProviderPicker.SelectedItem = current;
        UpdateLink(current);

        ProviderPicker.SelectedIndexChanged += (_, _) =>
        {
            if (ProviderPicker.SelectedItem is AiProvider p) UpdateLink(p);
        };
    }

    private void UpdateLink(AiProvider provider)
    {
        WebsiteLink.Text = string.IsNullOrEmpty(provider.Website) ? "" : $"申请 API Key → {provider.Website}";
    }

    private async void OnWebsiteTapped(object? sender, EventArgs e)
    {
        var url = (ProviderPicker.SelectedItem as AiProvider)?.Website;
        if (string.IsNullOrEmpty(url)) return;
        try { await Browser.OpenAsync(url, BrowserLaunchMode.External); } catch { }
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        var apiKey = ApiKeyEntry.Text?.Trim() ?? "";
        var provider = ProviderPicker.SelectedItem as AiProvider ?? ProviderRegistry.OpenAI;
        var wakeWord = WakeWordEntry.Text?.Trim();
        if (string.IsNullOrEmpty(wakeWord)) wakeWord = "滴墨";

        // 保存 AI 配置
        _config.ApiKey = apiKey;
        _config.ProviderKey = provider.Key;
        _config.Save();
        Preferences.Set("openai_api_key", apiKey);

        // 保存语音配置
        Preferences.Set("wake_word", wakeWord);
        Preferences.Set("voice_wake_enabled", VoiceWakeSwitch.IsToggled);

        // 标记首次启动已完成
        Preferences.Set("setup_completed", true);

        // 切换到主 Shell
        Application.Current!.MainPage = new AppShell();
    }
}
