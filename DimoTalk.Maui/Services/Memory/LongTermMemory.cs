using System.Numerics;
using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using Microsoft.Data.Sqlite;

namespace DimoTalk.Maui.Services.Memory;

public class LongTermMemory
{
    private readonly SqliteConnection _conn;

    public LongTermMemory(SqliteConnection conn)
    {
        _conn = conn;
        Init();
    }

    private void Init()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
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
        """;
        cmd.ExecuteNonQuery();
    }

    public void Store(string userId, string content, float[] embedding, string? sourceMessageId = null, double confidence = 1.0)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO long_term_memories
                (user_id, content, embedding, source_message_id, confidence, created_at)
            VALUES (@uid, @content, @emb, @src, @conf, @ts)
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@emb", FloatArrayToBytes(embedding));
        cmd.Parameters.AddWithValue("@src", (object?)sourceMessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@conf", confidence);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    public List<MemoryHit> Recall(string userId, float[] queryEmbedding, int topK = AppConfig.LongTermTopK, double threshold = AppConfig.LongTermSimilarityThreshold)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, content, confidence, last_accessed_at, embedding
            FROM long_term_memories WHERE user_id = @uid
        """;
        cmd.Parameters.AddWithValue("@uid", userId);

        var hits = new List<MemoryHit>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var storedEmbedding = BytesToFloatArray((byte[])reader["embedding"]);
            var distance = CosineDistance(queryEmbedding, storedEmbedding);
            if (distance >= threshold) continue;

            var id = reader.GetInt32(0);
            hits.Add(new MemoryHit
            {
                Id = id,
                Content = reader.GetString(1),
                Confidence = reader.GetDouble(2),
                LastAccessedAt = reader.IsDBNull(3) ? DateTime.MinValue : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)).UtcDateTime,
                Distance = distance
            });

            UpdateLastAccessed(id, now);
        }

        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits.Take(topK).ToList();
    }

    public void ForgetExpired(int days = 90)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM long_term_memories
            WHERE confidence < 0.5
              AND (last_accessed_at IS NULL OR last_accessed_at < @cutoff)
        """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        cmd.ExecuteNonQuery();
    }

    private void UpdateLastAccessed(int id, long timestamp)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE long_term_memories SET last_accessed_at = @ts WHERE id = @id";
        cmd.Parameters.AddWithValue("@ts", timestamp);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 1.0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 1.0;
        return 1.0 - (dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private static byte[] FloatArrayToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloatArray(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
