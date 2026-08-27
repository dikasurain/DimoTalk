using Microsoft.Extensions.DependencyInjection;
using DimoTalk.Maui.Pages;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using DimoTalk.Maui.Services.Voice;

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

        // MemoryManager（单例，延迟初始化）
        var memoryManager = new Lazy<MemoryManager?>(() => MemoryManager.CreateAsync().GetAwaiter().GetResult());
        builder.Services.AddSingleton(sp => memoryManager.Value);

        // AI 客户端：内部按需读取最新配置，无需 DI 缓存
        builder.Services.AddSingleton<OpenAIClient>();
        builder.Services.AddSingleton<IAsrService, WhisperAsrService>();
        builder.Services.AddSingleton<ITtsService, OpenAITtsService>();
        builder.Services.AddSingleton<IAudioPlayer, AudioPlayer>();
        builder.Services.AddSingleton<IWakeWordDetector, VoskWakeWordDetector>();

        // ChatService：取最新 OpenAIClient（注入的实例会读取最新 Preferences）
        builder.Services.AddSingleton<ChatService>(sp =>
        {
            var mm = sp.GetService<MemoryManager?>();
            var ai = sp.GetRequiredService<OpenAIClient>();
            if (mm == null) return null!;
            return new ChatService(mm, ai);
        });

        // VoiceConversationManager
        builder.Services.AddSingleton<VoiceConversationManager>(sp =>
        {
            var mm = sp.GetService<MemoryManager?>();
            var ai = sp.GetRequiredService<OpenAIClient>();
            if (mm == null) return null!;
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

        return builder.Build();
    }
}
