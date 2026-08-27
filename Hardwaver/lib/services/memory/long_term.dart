import 'dart:math';
import 'dart:typed_data';
import 'package:sqlite3/sqlite3.dart';
import '../../models/memory_hit.dart';
import '../../config/app_config.dart';

class LongTermMemory {
  final Database _db;

  LongTermMemory(this._db);

  static void init(Database db) {
    db.execute('''
      CREATE TABLE IF NOT EXISTS long_term_memories (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id TEXT NOT NULL,
        content TEXT NOT NULL,
        embedding BLOB NOT NULL,
        source_message_id TEXT,
        confidence REAL DEFAULT 1.0,
        last_accessed_at INTEGER,
        created_at INTEGER NOT NULL
      );
    ''');
    db.execute('''
      CREATE INDEX IF NOT EXISTS idx_ltm_user
        ON long_term_memories(user_id);
    ''');
  }

  Future<void> store({
    required String userId,
    required String content,
    required List<double> embedding,
    String? sourceMessageId,
    double confidence = 1.0,
  }) async {
    final blob = _float32ListToBytes(embedding);
    _db.execute(
      'INSERT INTO long_term_memories (user_id, content, embedding, source_message_id, confidence, created_at) VALUES (?, ?, ?, ?, ?, ?)',
      [
        userId,
        content,
        blob,
        sourceMessageId,
        confidence,
        DateTime.now().millisecondsSinceEpoch,
      ],
    );
  }

  Future<List<MemoryHit>> recall({
    required String userId,
    required List<double> queryEmbedding,
    int topK = AppConfig.longTermTopK,
    double threshold = AppConfig.longTermSimilarityThreshold,
  }) async {
    final rs = _db.select(
      'SELECT id, content, confidence, last_accessed_at, embedding FROM long_term_memories WHERE user_id = ?',
      [userId],
    );

    final hits = <MemoryHit>[];
    final now = DateTime.now().millisecondsSinceEpoch;

    for (final row in rs) {
      final storedEmbedding = _bytesToFloat32List(row['embedding'] as Uint8List);
      final distance = _cosineDistance(queryEmbedding, storedEmbedding);
      if (distance >= threshold) continue;

      hits.add(MemoryHit(
        id: row['id'] as int,
        content: row['content'] as String,
        distance: distance,
        confidence: (row['confidence'] as num).toDouble(),
        lastAccessedAt: DateTime.fromMillisecondsSinceEpoch(
          row['last_accessed_at'] as int? ?? 0,
        ),
      ));

      _db.execute(
        'UPDATE long_term_memories SET last_accessed_at = ? WHERE id = ?',
        [now, row['id']],
      );
    }

    hits.sort((a, b) => a.distance.compareTo(b.distance));
    return hits.take(topK).toList();
  }

  Future<void> forgetExpired({int days = 90}) async {
    final cutoff = DateTime.now()
        .subtract(Duration(days: days))
        .millisecondsSinceEpoch;
    _db.execute(
      'DELETE FROM long_term_memories WHERE confidence < 0.5 AND (last_accessed_at IS NULL OR last_accessed_at < ?)',
      [cutoff],
    );
  }

  static double _cosineDistance(List<double> a, List<double> b) {
    if (a.length != b.length) return 1.0;
    double dot = 0, normA = 0, normB = 0;
    for (var i = 0; i < a.length; i++) {
      dot += a[i] * b[i];
      normA += a[i] * a[i];
      normB += b[i] * b[i];
    }
    if (normA == 0 || normB == 0) return 1.0;
    final similarity = dot / (sqrt(normA) * sqrt(normB));
    return 1.0 - similarity;
  }

  static Uint8List _float32ListToBytes(List<double> values) {
    final bytes = Uint8List(values.length * 4);
    final view = ByteData.sublistView(bytes);
    for (var i = 0; i < values.length; i++) {
      view.setFloat32(i * 4, values[i], Endian.little);
    }
    return bytes;
  }

  static List<double> _bytesToFloat32List(Uint8List bytes) {
    final count = bytes.length ~/ 4;
    final view = ByteData.sublistView(bytes);
    return List.generate(count, (i) => view.getFloat32(i * 4, Endian.little));
  }
}
