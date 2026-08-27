import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../providers/providers.dart';

class SettingsPage extends ConsumerStatefulWidget {
  const SettingsPage({super.key});

  @override
  ConsumerState<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends ConsumerState<SettingsPage> {
  final _keyController = TextEditingController();
  bool _obscured = true;

  @override
  void initState() {
    super.initState();
    Future.microtask(() async {
      await ref.read(settingsControllerProvider.notifier).loadApiKey();
      final key = ref.read(apiKeyProvider);
      if (key != null) _keyController.text = key;
    });
  }

  @override
  void dispose() {
    _keyController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('设置')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('OpenAI API Key',
              style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          TextField(
            controller: _keyController,
            obscureText: _obscured,
            decoration: InputDecoration(
              hintText: 'sk-...',
              border: const OutlineInputBorder(),
              suffixIcon: IconButton(
                icon: Icon(_obscured ? Icons.visibility_off : Icons.visibility),
                onPressed: () => setState(() => _obscured = !_obscured),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton(
                onPressed: () {
                  ref
                      .read(settingsControllerProvider.notifier)
                      .saveApiKey(_keyController.text.trim());
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('API Key 已保存')),
                  );
                },
                child: const Text('保存'),
              ),
              const SizedBox(width: 8),
              TextButton(
                onPressed: () {
                  _keyController.clear();
                  ref.read(settingsControllerProvider.notifier).clearApiKey();
                },
                child: const Text('清除'),
              ),
            ],
          ),
          const SizedBox(height: 24),
          const Divider(),
          const SizedBox(height: 8),
          Text('关于记忆系统',
              style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Text(
            '本应用使用三层记忆架构：\n'
            '• 短期：当前对话上下文窗口（最近 20 条）\n'
            '• 中期：会话结束后自动生成摘要存入 SQLite\n'
            '• 长期：从对话中提取的用户偏好/事实，向量检索',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}
