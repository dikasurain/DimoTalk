import 'package:sqlite3/sqlite3.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'dart:ffi';
import 'package:sqlite3/open.dart';

import '../../config/app_config.dart';
import '../../models/message.dart';
import 'short_term.dart';
import 'mid_term.dart';
import 'long_term.dart';

class MemoryManager {
  final ShortTermMemory _shortTerm;
  final MidTermMemory _midTerm;
  final LongTermMemory _longTerm;
  final Database _db;

  MemoryManager._({
    required ShortTermMemory shortTerm,
    required MidTermMemory midTerm,
    required LongTermMemory longTerm,
    required Database db,
  })  : _shortTerm = shortTerm,
        _midTerm = midTerm,
        _longTerm = longTerm,
        _db = db;

  static Future<MemoryManager> create({
    int shortTermMaxSize = AppConfig.shortTermMaxMessages,
  }) async {
    open.overrideFor(OperatingSystem.windows, () => DynamicLibrary.open('sqlite3.dll'));

    final dir = await getApplicationDocumentsDirectory();
    final dbPath = p.join(dir.path, 'dimotalk_memory.db');
    final db = sqlite3.open(dbPath);

    db.execute('PRAGMA journal_mode=WAL;');
    db.execute('PRAGMA foreign_keys=ON;');

    MidTermMemory.init(db);
    LongTermMemory.init(db);

    final shortTerm = ShortTermMemory(maxSize: shortTermMaxSize);
    final midTerm = MidTermMemory(db);
    final longTerm = LongTermMemory(db);

    longTerm.forgetExpired();

    return MemoryManager._(
      shortTerm: shortTerm,
      midTerm: midTerm,
      longTerm: longTerm,
      db: db,
    );
  }

  ShortTermMemory get shortTerm => _shortTerm;
  MidTermMemory get midTerm => _midTerm;
  LongTermMemory get longTerm => _longTerm;

  void addToShortTerm(Message msg) => _shortTerm.add(msg);

  Future<void> onSessionEnd({
    required String conversationId,
    required String userId,
    required String summary,
  }) async {
    await _midTerm.storeSummary(
      conversationId: conversationId,
      userId: userId,
      summary: summary,
    );
    _shortTerm.clear();
  }

  Future<void> dispose() async {
    _db.dispose();
  }
}
