namespace DimoTalk.Maui.Config;

public static class AppConfig
{
    public const string AppName = "滴墨讲";

    public const string DefaultModel = "gpt-4o-mini";
    public const string DefaultEmbeddingModel = "text-embedding-3-small";
    public const string DefaultWhisperModel = "whisper-1";
    public const string DefaultTtsModel = "tts-1";

    public const int ShortTermMaxMessages = 20;
    public const int LongTermTopK = 5;
    public const double LongTermSimilarityThreshold = 0.3;
    public const int MidTermRecallLimit = 3;
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
}
