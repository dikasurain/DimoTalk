import 'package:logger/logger.dart';
import 'memory/memory_manager.dart';
import 'ai/openai_client.dart';
import 'ai/prompt_builder.dart';
import '../models/message.dart';
import '../models/memory_hit.dart';
import '../config/app_config.dart';

class ChatService {
  final MemoryManager _memoryManager;
  final OpenAIClient _ai;
  final Logger _logger = Logger();

  ChatService({
    required MemoryManager memoryManager,
    required OpenAIClient ai,
  })  : _memoryManager = memoryManager,
        _ai = ai;

  Future<String> sendMessage({
    required String userId,
    required String userInput,
  }) async {
    final userMsg = Message(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      role: MessageRole.user,
      content: userInput,
      timestamp: DateTime.now(),
    );
    _memoryManager.addToShortTerm(userMsg);

    final ltmFuture = _ai
        .embed(input: userInput)
        .then((e) => _memoryManager.longTerm.recall(
              userId: userId,
              queryEmbedding: e,
            ))
        .catchError((_) => <MemoryHit>[]);

    final midFuture = _memoryManager.midTerm.recall(
      userId,
      limit: AppConfig.midTermRecallLimit,
    );

    final results = await Future.wait([ltmFuture, midFuture]);
    final ltmHits = results[0] as List<MemoryHit>;
    final midSummaries = results[1] as List<String>;

    final messages = PromptBuilder.toOpenAIMessages(
      userInput: userInput,
      shortTerm: _memoryManager.shortTerm,
      midTermSummaries: midSummaries,
      longTermHits: ltmHits,
    );

    final reply = await _ai.chat(messages: messages);

    final assistantMsg = Message(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      role: MessageRole.assistant,
      content: reply,
      timestamp: DateTime.now(),
    );
    _memoryManager.addToShortTerm(assistantMsg);

    _tryExtractToLongTerm(userId, userInput);

    return reply;
  }

  void _tryExtractToLongTerm(String userId, String userInput) {
    Future.microtask(() async {
      try {
        final shouldExtract = await _ai.shouldExtractToLongTerm(userInput);
        if (!shouldExtract) return;

        final fact = await _ai.extractKeyFact(userInput);
        if (fact == null || fact.isEmpty) return;

        final embedding = await _ai.embed(input: fact);
        await _memoryManager.longTerm.store(
          userId: userId,
          content: fact,
          embedding: embedding,
        );
        _logger.i('长期记忆已写入: $fact');
      } catch (e) {
        _logger.w('长期记忆提取失败: $e');
      }
    });
  }

  Future<void> finalizeSession({
    required String conversationId,
    required String userId,
  }) async {
    final shortTerm = _memoryManager.shortTerm.context;
    if (shortTerm.isEmpty) return;

    final userMsgs = shortTerm
        .where((m) => m.role == MessageRole.user)
        .map((m) => m.content)
        .toList();
    final assistantMsgs = shortTerm
        .where((m) => m.role == MessageRole.assistant)
        .map((m) => m.content)
        .toList();

    final summary = await _ai.summarizeConversation(
      userMessages: userMsgs,
      assistantReplies: assistantMsgs,
    );

    await _memoryManager.onSessionEnd(
      conversationId: conversationId,
      userId: userId,
      summary: summary,
    );
  }
}
