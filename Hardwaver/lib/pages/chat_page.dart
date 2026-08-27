import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../models/message.dart';
import '../providers/providers.dart';

class ChatPage extends ConsumerStatefulWidget {
  const ChatPage({super.key});

  @override
  ConsumerState<ChatPage> createState() => _ChatPageState();
}

class _ChatPageState extends ConsumerState<ChatPage> {
  final _controller = TextEditingController();
  final _scrollController = ScrollController();
  final List<Message> _messages = [];
  bool _isSending = false;

  @override
  void initState() {
    super.initState();
    Future.microtask(() {
      ref.read(settingsControllerProvider.notifier).loadApiKey();
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scrollController.hasClients) return;
      _scrollController.animateTo(
        _scrollController.position.maxScrollExtent,
        duration: const Duration(milliseconds: 200),
        curve: Curves.easeOut,
      );
    });
  }

  Future<void> _send() async {
    final text = _controller.text.trim();
    if (text.isEmpty || _isSending) return;

    final chatService = ref.read(chatServiceProvider);
    if (chatService == null) {
      _showSnack('请先在设置中配置 OpenAI API Key');
      return;
    }

    setState(() {
      _isSending = true;
      _messages.add(Message(
        id: const Uuid().v4(),
        role: MessageRole.user,
        content: text,
        timestamp: DateTime.now(),
      ));
    });
    _controller.clear();
    _scrollToBottom();

    try {
      final reply = await chatService.sendMessage(
        userId: ref.read(userIdProvider),
        userInput: text,
      );
      setState(() {
        _messages.add(Message(
          id: const Uuid().v4(),
          role: MessageRole.assistant,
          content: reply,
          timestamp: DateTime.now(),
        ));
      });
    } catch (e) {
      _showSnack('发送失败: $e');
    } finally {
      setState(() => _isSending = false);
      _scrollToBottom();
    }
  }

  void _showSnack(String msg) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(msg)));
  }

  @override
  Widget build(BuildContext context) {
    final hasKey = ref.watch(apiKeyProvider) != null;

    return Scaffold(
      appBar: AppBar(title: const Text('滴墨讲')),
      body: Column(
        children: [
          Expanded(child: _buildMessageList(hasKey)),
          _buildInputBar(),
        ],
      ),
    );
  }

  Widget _buildMessageList(bool hasKey) {
    if (_messages.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.chat_bubble_outline,
                size: 64, color: Colors.pink.shade200),
            const SizedBox(height: 16),
            Text(
              hasKey ? '开始对话吧' : '请先在设置中配置 API Key',
              style: Theme.of(context).textTheme.bodyLarge,
            ),
          ],
        ),
      );
    }

    return ListView.builder(
      controller: _scrollController,
      padding: const EdgeInsets.all(12),
      itemCount: _messages.length,
      itemBuilder: (_, i) => _Bubble(_messages[i]),
    );
  }

  Widget _buildInputBar() {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(8, 4, 8, 8),
        child: Row(
          children: [
            Expanded(
              child: TextField(
                controller: _controller,
                decoration: const InputDecoration(
                  hintText: '输入消息...',
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.all(Radius.circular(24)),
                  ),
                  contentPadding:
                      EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                ),
                onSubmitted: (_) => _send(),
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filled(
              onPressed: _isSending ? null : _send,
              icon: _isSending
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.send),
            ),
          ],
        ),
      ),
    );
  }
}

class _Bubble extends StatelessWidget {
  final Message msg;
  const _Bubble(this.msg);

  @override
  Widget build(BuildContext context) {
    final isUser = msg.role == MessageRole.user;
    final align = isUser ? Alignment.centerRight : Alignment.centerLeft;
    final color = isUser ? Colors.pink.shade500 : Colors.grey.shade200;
    final textColor = isUser ? Colors.white : Colors.black87;

    return Container(
      alignment: align,
      margin: const EdgeInsets.symmetric(vertical: 4),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        constraints: BoxConstraints(
            maxWidth: MediaQuery.of(context).size.width * 0.75),
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Text(
          msg.content,
          style: TextStyle(color: textColor, fontSize: 15),
        ),
      ),
    );
  }
}
