using DimoTalk.Maui.Config;
using Microsoft.Data.Sqlite;

namespace DimoTalk.Maui.Services.Memory;

public class MidTermMemory
{
    private readonly SqliteConnection _conn;

    public MidTermMemory(SqliteConnection conn)
    {
        _conn = conn;
        Init();
    }

    private void Init()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS conversation_summaries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                summary TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                topic TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_summaries_user_time
                ON conversation_summaries(user_id, created_at);
        """;
        cmd.ExecuteNonQuery();
    }

    public void StoreSummary(string conversationId, string userId, string summary, string? topic = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_summaries
                (conversation_id, user_id, summary, created_at, topic)
            VALUES (@cid, @uid, @sum, @ts, @topic)
        """;
        cmd.Parameters.AddWithValue("@cid", conversationId);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@sum", summary);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@topic", (object?)topic ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<string> Recall(string userId, int limit = AppConfig.MidTermRecallLimit)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT summary FROM conversation_summaries
            WHERE user_id = @uid
            ORDER BY created_at DESC
            LIMIT @limit
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }
}
