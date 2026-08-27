class MemoryHit {
  final int id;
  final String content;
  final double distance;
  final double confidence;
  final DateTime lastAccessedAt;

  const MemoryHit({
    required this.id,
    required this.content,
    required this.distance,
    required this.confidence,
    required this.lastAccessedAt,
  });

  factory MemoryHit.fromMap(Map<String, dynamic> map) => MemoryHit(
        id: map['id'] as int,
        content: map['content'] as String,
        distance: (map['distance'] as num).toDouble(),
        confidence: (map['confidence'] as num).toDouble(),
        lastAccessedAt: DateTime.fromMillisecondsSinceEpoch(map['last_accessed_at'] as int? ?? 0),
      );
}
