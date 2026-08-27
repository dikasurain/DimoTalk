using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;
using Microsoft.Data.Sqlite;

namespace DimoTalk.Maui.Services.Memory;

public class MemoryManager : IDisposable
{
    public ShortTermMemory ShortTerm { get; }
    public MidTermMemory MidTerm { get; }
    public LongTermMemory LongTerm { get; }
    private readonly SqliteConnection _conn;

    private MemoryManager(ShortTermMemory shortTerm, MidTermMemory midTerm, LongTermMemory longTerm, SqliteConnection conn)
    {
        ShortTerm = shortTerm;
        MidTerm = midTerm;
        LongTerm = longTerm;
        _conn = conn;
    }

    public static async Task<MemoryManager> CreateAsync(string? userId = null, int shortTermMaxSize = AppConfig.ShortTermMaxMessages)
    {
        SQLitePCL.Batteries_V2.Init();

        var dir = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "dimotalk_memory.db");

        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        var shortTerm = new ShortTermMemory(shortTermMaxSize);
        var midTerm = new MidTermMemory(conn);
        var longTerm = new LongTermMemory(conn);
        longTerm.ForgetExpired();

        return new MemoryManager(shortTerm, midTerm, longTerm, conn);
    }

    public void AddToShortTerm(Message msg) => ShortTerm.Add(msg);

    public async Task OnSessionEndAsync(string conversationId, string userId, string summary)
    {
        MidTerm.StoreSummary(conversationId, userId, summary);
        ShortTerm.Clear();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
