using DimoTalk.Maui.Services.Memory;
using Microsoft.Data.Sqlite;

namespace DimoTalk.Maui.Services;

/// <summary>自传章节</summary>
public record ChapterInfo(int Index, string Title, string Content, string CreatedAt);

/// <summary>
/// 自传章节 + 主人公画像的 SQLite 存储
/// 表挂在 dimotalk_memory.db，由 MemoryManager 初始化连接时一并建表
/// </summary>
public class AutobiographyStore
{
    private readonly SqliteConnection _conn;

    public AutobiographyStore(SqliteConnection conn)
    {
        _conn = conn;
        Init();
    }

    private void Init()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS autobiography_chapters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                idx INTEGER NOT NULL,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now','localtime'))
            );
            CREATE INDEX IF NOT EXISTS idx_auto_user ON autobiography_chapters(user_id);

            CREATE TABLE IF NOT EXISTS protagonist_profile (
                user_id TEXT PRIMARY KEY,
                profile_json TEXT NOT NULL,
                updated_at TEXT DEFAULT (datetime('now','localtime'))
            );
        """;
        cmd.ExecuteNonQuery();
    }

    // ── 章节 ──

    public void SaveChapters(string userId, IEnumerable<(int Index, string Title, string Content)> chapters)
    {
        DeleteChapters(userId);
        using var tx = _conn.BeginTransaction();
        foreach (var (idx, title, content) in chapters)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO autobiography_chapters (user_id, idx, title, content)
                VALUES (@uid, @idx, @title, @content)
            """;
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@idx", idx);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<ChapterInfo> LoadChapters(string userId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT idx, title, content, created_at FROM autobiography_chapters
            WHERE user_id = @uid ORDER BY idx
        """;
        cmd.Parameters.AddWithValue("@uid", userId);

        var list = new List<ChapterInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new ChapterInfo(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return list;
    }

    public void DeleteChapters(string userId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM autobiography_chapters WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    // ── 画像 ──

    public void SaveProfile(string userId, ProtagonistProfile profile)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO protagonist_profile (user_id, profile_json, updated_at)
            VALUES (@uid, @json, datetime('now','localtime'))
            ON CONFLICT(user_id) DO UPDATE SET profile_json = @json, updated_at = datetime('now','localtime')
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@json", System.Text.Json.JsonSerializer.Serialize(profile));
        cmd.ExecuteNonQuery();
    }

    public ProtagonistProfile? LoadProfile(string userId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT profile_json FROM protagonist_profile WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ProtagonistProfile>(json);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteProfile(string userId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM protagonist_profile WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }
}
