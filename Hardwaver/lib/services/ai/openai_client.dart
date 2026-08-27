import 'package:dio/dio.dart';
import 'package:logger/logger.dart';
import '../../config/app_config.dart';

class OpenAIClient {
  final String apiKey;
  final String baseUrl;
  final Dio _dio;
  final Logger _logger = Logger();

  OpenAIClient({
    required this.apiKey,
    this.baseUrl = AppConfig.openaiBaseUrl,
  })  : _dio = Dio(BaseOptions(
          baseUrl: baseUrl,
          headers: {
            'Authorization': 'Bearer $apiKey',
            'Content-Type': 'application/json',
          },
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 60),
        ));

  Future<String> chat({
    required List<Map<String, dynamic>> messages,
    String model = AppConfig.defaultModel,
    double temperature = 0.7,
  }) async {
    try {
      final resp = await _dio.post(
        '/chat/completions',
        data: {
          'model': model,
          'messages': messages,
          'temperature': temperature,
        },
      );
      final content =
          resp.data['choices'][0]['message']['content'] as String;
      return content;
    } on DioException catch (e) {
      _logger.e('OpenAI chat failed: ${e.message}');
      rethrow;
    }
  }

  Future<List<double>> embed({
    required String input,
    String model = AppConfig.defaultEmbeddingModel,
  }) async {
    try {
      final resp = await _dio.post(
        '/embeddings',
        data: {
          'model': model,
          'input': input,
        },
      );
      final list = resp.data['data'][0]['embedding'] as List;
      return list.map((e) => (e as num).toDouble()).toList();
    } on DioException catch (e) {
      _logger.e('OpenAI embed failed: ${e.message}');
      rethrow;
    }
  }

  Future<String> summarizeConversation({
    required List<String> userMessages,
    required List<String> assistantReplies,
  }) async {
    final buffer = StringBuffer();
    for (var i = 0; i < userMessages.length; i++) {
      buffer.writeln('用户: ${userMessages[i]}');
      if (i < assistantReplies.length) {
        buffer.writeln('AI: ${assistantReplies[i]}');
      }
    }
    final prompt = '''
请将以下对话压缩为 200-500 字的摘要，保留：
1. 对话主题和关键进展
2. 用户表达的偏好、身份、事实
3. 未解决的问题

对话内容:
$buffer''';

    final result = await chat(
      messages: [
        {'role': 'system', 'content': '你是一个精准的对话摘要助手。'},
        {'role': 'user', 'content': prompt},
      ],
      model: AppConfig.defaultModel,
      temperature: 0.3,
    );
    return result;
  }

  Future<bool> shouldExtractToLongTerm(String message) async {
    final keywords = [
      '我叫', '我是', '我喜欢', '我讨厌', '我住在', '我来自',
      '我在', '我有', '我的', '我想', '我希望', '我以后',
    ];
    for (final kw in keywords) {
      if (message.contains(kw)) return true;
    }

    final result = await chat(
      messages: [
        {
          'role': 'system',
          'content':
              '判断用户消息是否包含值得长期记忆的信息（偏好、身份、事实、计划等）。只回答"是"或"否"。',
        },
        {'role': 'user', 'content': message},
      ],
      model: AppConfig.defaultModel,
      temperature: 0,
    );
    return result.trim().contains('是');
  }

  Future<String?> extractKeyFact(String message) async {
    final result = await chat(
      messages: [
        {
          'role': 'system',
          'content':
              '从用户消息中提取一条值得长期记忆的关键事实，用简洁的陈述句表达。如果没有则回答"无"。',
        },
        {'role': 'user', 'content': message},
      ],
      model: AppConfig.defaultModel,
      temperature: 0,
    );
    final text = result.trim();
    return text == '无' ? null : text;
  }
}
