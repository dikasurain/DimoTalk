using DimoTalk.Maui.Pages;

namespace DimoTalk.Maui;

public partial class App : Application
{
    public App(AppShell shell)
    {
        InitializeComponent();

        // 首次启动显示引导页，否则直接进入主界面
        var setupCompleted = Preferences.Get("setup_completed", false);
        MainPage = setupCompleted ? (Page)shell : new WelcomePage();
    }
}
