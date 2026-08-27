class AppConfig {
  static const String appName = '滴墨讲';

  static const String openaiBaseUrl = 'https://api.openai.com/v1';
  static const String defaultModel = 'gpt-4o-mini';
  static const String defaultEmbeddingModel = 'text-embedding-3-small';
  static const String defaultWhisperModel = 'whisper-1';
  static const String defaultTtsModel = 'tts-1';

  static const int shortTermMaxMessages = 20;
  static const int longTermTopK = 5;
  static const double longTermSimilarityThreshold = 0.3;
  static const int midTermRecallLimit = 3;
  static const Duration sessionTimeout = Duration(minutes: 30);
}
