using Microsoft.Extensions.DependencyInjection;
using DimoTalk.Maui.Pages;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using DimoTalk.Maui.Services.Voice;

namespace DimoTalk.Maui;

public static class MauiProgram
{
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

        _ = InitializeMemoryAsync();

        // ===== AI =====
        builder.Services.AddSingleton<OpenAIClient>();
        builder.Services.AddSingleton<IAsrService, WhisperAsrService>();
        builder.Services.AddSingleton<ITtsService, OpenAITtsService>();

        // ===== Voice（跨平台原生实现，单一 ContinuousAudioCapture 为所有消费者提供 PCM 流）=====
        builder.Services.AddSingleton<ContinuousAudioCapture>();
        builder.Services.AddSingleton<VoiceRecorder>();
        builder.Services.AddSingleton<AudioPlayer>();
        builder.Services.AddSingleton<IWakeWordDetector, VoskWakeWordDetector>();

        // ChatService + AutobiographyService（均依赖 MemoryManager；ChatService 借 AutobiographyService 生成日记）
        builder.Services.AddSingleton<AutobiographyService>(sp =>
        {
            var ai = sp.GetRequiredService<OpenAIClient>();
            return new AutobiographyService(MemoryInstance!, ai);
        });
        builder.Services.AddSingleton<ChatService>(sp =>
        {
            var ai = sp.GetRequiredService<OpenAIClient>();
            var auto = sp.GetService<AutobiographyService>();
            return new ChatService(MemoryInstance, ai, auto);
        });

        builder.Services.AddSingleton<VoiceConversationManager>(sp =>
        {
            var capture = sp.GetRequiredService<ContinuousAudioCapture>();
            var wake = sp.GetRequiredService<IWakeWordDetector>();
            var asr = sp.GetRequiredService<IAsrService>();
            var tts = sp.GetRequiredService<ITtsService>();
            var player = sp.GetRequiredService<AudioPlayer>();
            var chat = sp.GetRequiredService<ChatService>();
            var recorder = sp.GetRequiredService<VoiceRecorder>();
            return new VoiceConversationManager(capture, wake, asr, tts, player, chat, recorder);
        });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<ChatPage>(sp =>
        {
            var chatService = sp.GetService<ChatService>();
            var voice = sp.GetService<VoiceConversationManager>();
            return new ChatPage(chatService, voice, () => Preferences.Get("openai_api_key", string.Empty));
        });
        builder.Services.AddSingleton<MemoryPage>(sp =>
        {
            var ai = sp.GetRequiredService<OpenAIClient>();
            var auto = sp.GetService<AutobiographyService>();
            return new MemoryPage(MemoryInstance, auto, ai, () => Preferences.Get("openai_api_key", string.Empty));
        });
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();
        return app;
    }
}
