import 'package:uuid/uuid.dart';
import 'message.dart';

class Conversation {
  final String id;
  final String userId;
  final String title;
  final List<Message> messages;
  final DateTime createdAt;
  DateTime updatedAt;

  Conversation({
    String? id,
    required this.userId,
    this.title = '新对话',
    List<Message>? messages,
    DateTime? createdAt,
    DateTime? updatedAt,
  })  : id = id ?? const Uuid().v4(),
        messages = messages ?? [],
        createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? DateTime.now();

  void addMessage(Message msg) {
    messages.add(msg);
    updatedAt = DateTime.now();
  }
}
