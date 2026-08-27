using Microsoft.Extensions.DependencyInjection;
using DimoTalk.Maui.Pages;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using DimoTalk.Maui.Services.Voice;

namespace DimoTalk.Maui;

public static class MauiProgram
{
    // 全局共享：异步初始化的 MemoryManager，未就绪时为 null
    // 通过 InitializeMemoryAsync() 启动后台初始化，避免阻塞 UI 线程导致启动崩溃
    public static MemoryManager? MemoryInstance { get; private set; }

    public static async Task InitializeMemoryAsync()
    {
        if (MemoryInstance != null) return;
        try
        {
            MemoryInstance = await MemoryManager.CreateAsync();
            System.Diagnostics.Debug.WriteLine("MemoryManager 初始化成功");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MemoryManager 初始化失败: {ex}");
            // 不抛错，让 UI 能正常显示。功能不可用但不闪退
        }
    }

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

        // MemoryManager 不通过 DI 注入（避免 Shell 模板解析时同步阻塞 UI 线程）
        // 改为全局静态 + 异步初始化，UI 启动后由 App.OnStart 触发

        builder.Services.AddSingleton<OpenAIClient>();
        builder.Services.AddSingleton<IAsrService, WhisperAsrService>();
        builder.Services.AddSingleton<ITtsService, OpenAITtsService>();
        builder.Services.AddSingleton<IAudioPlayer, AudioPlayer>();
        builder.Services.AddSingleton<IWakeWordDetector, VoskWakeWordDetector>();

        // ChatService 用工厂方法，启动时 MemoryInstance 可能未就绪，返回降级实例
        builder.Services.AddSingleton<ChatService>(sp =>
        {
            var ai = sp.GetRequiredService<OpenAIClient>();
            return new ChatService(MemoryInstance, ai);
        });

        builder.Services.AddSingleton<VoiceConversationManager>(sp =>
        {
            var ai = sp.GetRequiredService<OpenAIClient>();
            var wake = sp.GetRequiredService<IWakeWordDetector>();
            var asr = sp.GetRequiredService<IAsrService>();
            var tts = sp.GetRequiredService<ITtsService>();
            var player = sp.GetRequiredService<IAudioPlayer>();
            var chat = sp.GetRequiredService<ChatService>();
            return new VoiceConversationManager(wake, asr, tts, player, chat);
        });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<ChatPage>(sp =>
        {
            var chatService = sp.GetService<ChatService>();
            var voice = sp.GetService<VoiceConversationManager>();
            return new ChatPage(chatService, voice, () => Preferences.Get("openai_api_key", string.Empty));
        });
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();

        // 启动后台异步初始化 MemoryManager（不阻塞 UI）
        _ = InitializeMemoryAsync();

        return app;
    }
}
