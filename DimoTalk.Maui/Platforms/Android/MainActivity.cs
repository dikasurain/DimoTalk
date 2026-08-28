using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace DimoTalk.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 状态栏浓墨色 + 白色前景图标，与水墨主题统一（覆盖 MAUI 默认品牌紫）
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#1C1A17"));

            // 清除 LightStatusBar 标记 → 状态栏图标保持白色（深色底）
            var decor = Window?.DecorView;
            if (decor != null)
            {
                var vis = decor.SystemUiVisibility & ~(StatusBarVisibility)SystemUiFlags.LightStatusBar;
                decor.SystemUiVisibility = vis;
            }
        }
    }
}
