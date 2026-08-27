namespace DimoTalk.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        ApiKeyEntry.Text = Preferences.Get("openai_api_key", string.Empty);
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        Preferences.Set("openai_api_key", ApiKeyEntry.Text?.Trim() ?? string.Empty);
        DisplayAlert("提示", "API Key 已保存", "确定");
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        ApiKeyEntry.Text = string.Empty;
        Preferences.Remove("openai_api_key");
    }
}
