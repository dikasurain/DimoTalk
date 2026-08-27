import 'package:sqlite3/sqlite3.dart';

class MidTermMemory {
  final Database _db;

  MidTermMemory(this._db);

  static void init(Database db) {
    db.execute('''
      CREATE TABLE IF NOT EXISTS conversation_summaries (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        conversation_id TEXT NOT NULL,
        user_id TEXT NOT NULL,
        summary TEXT NOT NULL,
        created_at INTEGER NOT NULL,
        topic TEXT
      );
    ''');
    db.execute('''
      CREATE INDEX IF NOT EXISTS idx_summaries_user_time
        ON conversation_summaries(user_id, created_at);
    ''');
  }

  Future<void> storeSummary({
    required String conversationId,
    required String userId,
    required String summary,
    String? topic,
  }) async {
    _db.execute(
      'INSERT INTO conversation_summaries (conversation_id, user_id, summary, created_at, topic) VALUES (?, ?, ?, ?, ?)',
      [conversationId, userId, summary, DateTime.now().millisecondsSinceEpoch, topic],
    );
  }

  Future<List<String>> recall(String userId, {int limit = 3}) async {
    final rs = _db.select(
      'SELECT summary FROM conversation_summaries WHERE user_id = ? ORDER BY created_at DESC LIMIT ?',
      [userId, limit],
    );
    return rs.map((r) => r['summary'] as String).toList();
  }
}
