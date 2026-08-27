import '../../config/app_config.dart';
import '../../models/message.dart';
import '../../models/memory_hit.dart';
import '../memory/short_term.dart';

class PromptBuilder {
  static String build({
    required String userInput,
    required ShortTermMemory shortTerm,
    List<String> midTermSummaries = const [],
    List<MemoryHit> longTermHits = const [],
    String systemPrompt = '你是滴墨讲（DimoTalk），一个温暖贴心的 AI 伙伴。请用自然、友好的方式与用户对话。',
  }) {
    final buffer = StringBuffer();
    buffer.writeln(systemPrompt);

    if (longTermHits.isNotEmpty) {
      buffer.writeln('\n[你知道关于用户的以下信息，请在回答中自然运用]');
      for (final hit in longTermHits) {
        buffer.writeln('- ${hit.content}');
      }
    }

    if (midTermSummaries.isNotEmpty) {
      buffer.writeln('\n[最近对话回顾]');
      buffer.writeln(midTermSummaries.join('\n'));
    }

    buffer.writeln('\n[当前对话历史]');
    for (final msg in shortTerm.context) {
      buffer.writeln('${msg.role.name}: ${msg.content}');
    }

    return buffer.toString();
  }

  static List<Map<String, dynamic>> toOpenAIMessages({
    required String userInput,
    required ShortTermMemory shortTerm,
    List<String> midTermSummaries = const [],
    List<MemoryHit> longTermHits = const [],
    String systemPrompt = '你是滴墨讲（DimoTalk），一个温暖贴心的 AI 伙伴。请用自然、友好的方式与用户对话。',
  }) {
    final messages = <Map<String, dynamic>>[
      {'role': 'system', 'content': _assembleSystem(systemPrompt, midTermSummaries, longTermHits)},
    ];

    for (final msg in shortTerm.context) {
      messages.add(msg.toOpenAIMap());
    }
    messages.add({'role': 'user', 'content': userInput});

    return messages;
  }

  static String _assembleSystem(
    String base,
    List<String> midTerm,
    List<MemoryHit> longTerm,
  ) {
    final buffer = StringBuffer(base);
    if (longTerm.isNotEmpty) {
      buffer.writeln('\n[长期记忆 - 关于用户]');
      for (final hit in longTerm) {
        buffer.writeln('- ${hit.content}');
      }
    }
    if (midTerm.isNotEmpty) {
      buffer.writeln('\n[最近对话摘要]');
      buffer.writeln(midTerm.join('\n'));
    }
    return buffer.toString();
  }
}
