using Microsoft.Extensions.DependencyInjection;
using DimoTalk.Maui.Pages;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;

namespace DimoTalk.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 注册 MemoryManager (单例，延迟初始化)
        var memoryManager = new Lazy<MemoryManager?>(() => MemoryManager.CreateAsync().GetAwaiter().GetResult());
        builder.Services.AddSingleton(sp => memoryManager.Value);

        // OpenAIClientFactory: 每次请求时读取最新 API Key
        builder.Services.AddSingleton(sp =>
        {
            var key = Preferences.Get("openai_api_key", string.Empty);
            return string.IsNullOrEmpty(key) ? null : new OpenAIClient(key);
        });

        // ChatService 工厂: 每次取新的 ChatService 以读取最新 API Key
        builder.Services.AddSingleton<ChatService>(sp =>
        {
            var mm = sp.GetService<MemoryManager?>();
            var key = Preferences.Get("openai_api_key", string.Empty);
            if (mm == null || string.IsNullOrEmpty(key)) return null!;
            return new ChatService(mm, new OpenAIClient(key));
        });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<ChatPage>(sp =>
        {
            var chatService = sp.GetService<ChatService>();
            return new ChatPage(chatService, () => Preferences.Get("openai_api_key", string.Empty));
        });
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();
        return app;
    }
}
