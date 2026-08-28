using DimoTalk.Maui.Services.Memory;
using Microsoft.Data.Sqlite;

namespace DimoTalk.Maui.Services;

/// <summary>自传章节</summary>
public record ChapterInfo(int Index, string Title, string Content, string CreatedAt);

/// <summary>日记条目</summary>
public record DiaryInfo(string Date, string Content, string UpdatedAt);

/// <summary>聊天记录条目</summary>
public record ChatMessageRow(string Role, string Content, string Time);

/// <summary>某日对话统计</summary>
public record DayChatStats(int Count, string? FirstTime, string? LastTime);

/// <summary>
/// 自传章节 + 主人公画像 + 日记 + 聊天记录 的 SQLite 存储
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

            CREATE TABLE IF NOT EXISTS diary (
                user_id TEXT NOT NULL,
                date TEXT NOT NULL,
                content TEXT NOT NULL,
                updated_at TEXT DEFAULT (datetime('now','localtime')),
                PRIMARY KEY (user_id, date)
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now','localtime'))
            );
            CREATE INDEX IF NOT EXISTS idx_msg_user_time ON chat_messages(user_id, created_at);
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

    // ── 日记 ──

    /// <summary>按日期 upsert 日记（同一天多次生成 → 合并重写）</summary>
    public void SaveDiary(string userId, string date, string content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO diary (user_id, date, content, updated_at)
            VALUES (@uid, @date, @content, datetime('now','localtime'))
            ON CONFLICT(user_id, date) DO UPDATE SET
                content = @content, updated_at = datetime('now','localtime')
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.ExecuteNonQuery();
    }

    public DiaryInfo? LoadDiary(string userId, string date)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT date, content, updated_at FROM diary WHERE user_id = @uid AND date = @date";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@date", date);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new DiaryInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>最近 N 天有日记的日期（倒序）</summary>
    public List<DiaryInfo> LoadDiaryList(string userId, int limit = 30)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT date, content, updated_at FROM diary
            WHERE user_id = @uid ORDER BY date DESC LIMIT @limit
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<DiaryInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new DiaryInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }

    // ── 聊天记录 ──

    public void SaveMessage(string userId, string role, string content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_messages (user_id, role, content)
            VALUES (@uid, @role, @content)
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.ExecuteNonQuery();
    }

    /// <summary>最近 limit 条聊天记录（正序返回）</summary>
    public List<ChatMessageRow> LoadMessages(string userId, int limit = 100)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT role, content, created_at FROM chat_messages
            WHERE user_id = @uid ORDER BY id DESC LIMIT @limit
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<ChatMessageRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new ChatMessageRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        list.Reverse();
        return list;
    }

    /// <summary>某天的对话统计（条数 / 首末时间）</summary>
    public DayChatStats? LoadDayStats(string userId, string date)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), MIN(created_at), MAX(created_at) FROM chat_messages
            WHERE user_id = @uid AND date(created_at) = @date
        """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@date", date);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) == 0) return null;
        var first = reader.IsDBNull(1) ? null : reader.GetString(1);
        var last = reader.IsDBNull(2) ? null : reader.GetString(2);
        return new DayChatStats((int)reader.GetInt64(0), first, last);
    }
}
