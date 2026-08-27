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
    _db.insert('long_term_memories', {
      'user_id': userId,
      'content': content,
      'embedding': blob,
      'source_message_id': sourceMessageId,
      'confidence': confidence,
      'created_at': DateTime.now().millisecondsSinceEpoch,
    });
  }

  Future<List<MemoryHit>> recall({
    required String userId,
    required List<double> queryEmbedding,
    int topK = AppConfig.longTermTopK,
    double threshold = AppConfig.longTermSimilarityThreshold,
  }) async {
    final queryBlob = _float32ListToBytes(queryEmbedding);
    final rs = _db.select(
      _db.prepare('''
        SELECT id, content, confidence, last_accessed_at,
               cosine_distance(embedding, ?) AS distance
        FROM long_term_memories
        WHERE user_id = ? AND cosine_distance(embedding, ?) < ?
        ORDER BY distance ASC
        LIMIT ?
      '''),
      [queryBlob, userId, queryBlob, threshold, topK],
    );

    final now = DateTime.now().millisecondsSinceEpoch;
    final hits = <MemoryHit>[];
    final idsToUpdate = <int>[];

    for (final row in rs) {
      final hit = MemoryHit(
        id: row['id'] as int,
        content: row['content'] as String,
        distance: (row['distance'] as num).toDouble(),
        confidence: (row['confidence'] as num).toDouble(),
        lastAccessedAt: DateTime.fromMillisecondsSinceEpoch(
          row['last_accessed_at'] as int? ?? 0,
        ),
      );
      hits.add(hit);
      idsToUpdate.add(hit.id);
    }

    for (final id in idsToUpdate) {
      _db.execute(
        'UPDATE long_term_memories SET last_accessed_at = ? WHERE id = ?',
        [now, id],
      );
    }

    return hits;
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
