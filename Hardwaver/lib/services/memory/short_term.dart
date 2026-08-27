import '../../models/message.dart';

class ShortTermMemory {
  final List<Message> _window = [];
  final int maxSize;

  ShortTermMemory({this.maxSize = 20});

  void add(Message msg) {
    _window.add(msg);
    _evict();
  }

  void _evict() {
    while (_window.length > maxSize) {
      _window.removeAt(0);
    }
  }

  List<Message> get context => List.unmodifiable(_window);

  int get length => _window.length;

  void clear() => _window.clear();
}
