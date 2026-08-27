import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import '../services/chat_service.dart';
import '../services/ai/openai_client.dart';
import '../services/memory/memory_manager.dart';

final apiKeyProvider = StateProvider<String?>((ref) => null);

final memoryManagerProvider = FutureProvider<MemoryManager>((ref) async {
  final mm = await MemoryManager.create();
  ref.onDispose(mm.dispose);
  return mm;
});

final openAIClientProvider = Provider<OpenAIClient?>((ref) {
  final key = ref.watch(apiKeyProvider);
  if (key == null || key.isEmpty) return null;
  return OpenAIClient(apiKey: key);
});

final chatServiceProvider = Provider<ChatService?>((ref) {
  final mmAsync = ref.watch(memoryManagerProvider);
  final ai = ref.watch(openAIClientProvider);
  if (mmAsync.isLoading || mmAsync.hasError) return null;
  final mm = mmAsync.requireValue;
  if (ai == null) return null;
  return ChatService(memoryManager: mm, ai: ai);
});

final userIdProvider = Provider<String>((ref) {
  return const Uuid().v4();
});

final settingsControllerProvider =
    NotifierProvider<SettingsController, void>(SettingsController.new);

class SettingsController extends Notifier<void> {
  static const _kApiKey = 'openai_api_key';

  @override
  void build() {}

  Future<void> loadApiKey() async {
    final prefs = await SharedPreferences.getInstance();
    final key = prefs.getString(_kApiKey);
    if (key != null) {
      ref.read(apiKeyProvider.notifier).state = key;
    }
  }

  Future<void> saveApiKey(String key) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kApiKey, key);
    ref.read(apiKeyProvider.notifier).state = key;
  }

  Future<void> clearApiKey() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kApiKey);
    ref.read(apiKeyProvider.notifier).state = null;
  }
}
