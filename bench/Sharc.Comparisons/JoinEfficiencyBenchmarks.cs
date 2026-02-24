using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Query;

namespace Sharc.Comparisons;

[MemoryDiagnoser]
[BenchmarkCategory("JoinEfficiency")]
public class JoinEfficiencyBenchmarks
{
    private string _dbPath = null!;
    private SharcDatabase _db = null!;

    [Params(1000, 5000)]
    public int UserCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_join_{Guid.NewGuid()}.db");
        JoinDataGenerator.Generate(_dbPath, UserCount, 2); // 2 orders per user, no indexes
        var dbBytes = File.ReadAllBytes(_dbPath);
        _db = SharcDatabase.OpenMemory(dbBytes, new SharcOpenOptions { PageCacheSize = 1000 });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db?.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }

    [Benchmark(Baseline = true)]
    public int Join_FullMaterialization()
    {
        var sql = "SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id";
        using var reader = _db.Query(sql);
        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Join_WithSelectiveFilter()
    {
        var sql = "SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id WHERE u.id < 100";
        using var reader = _db.Query(sql);
        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int FullOuterJoin_TieredHashJoin()
    {
        var sql = "SELECT u.name, o.amount FROM users u FULL OUTER JOIN orders o ON u.id = o.user_id";
        using var reader = _db.Query(sql);
        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int LeftJoin_Baseline()
    {
        var sql = "SELECT u.name, o.amount FROM users u LEFT JOIN orders o ON u.id = o.user_id";
        using var reader = _db.Query(sql);
        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        return count;
    }
}

/// <summary>
/// Benchmarks comparing index-accelerated WHERE queries (IndexSeekCursor) vs full table scans,
/// with SQLite comparative numbers.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("IndexAccelerated")]
public class IndexAcceleratedBenchmarks
{
    private string _dbPath = null!;
    private SharcDatabase _dbIndexed = null!;
    private SharcDatabase _dbNoIndex = null!;
    private SqliteConnection _sqliteConn = null!;
    private int _seekUserId;

    [Params(5000)]
    public int UserCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Create indexed database
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_idx_{Guid.NewGuid()}.db");
        JoinDataGenerator.Generate(_dbPath, UserCount, 3, createIndexes: true);
        var dbBytes = File.ReadAllBytes(_dbPath);
        _dbIndexed = SharcDatabase.OpenMemory(dbBytes, new SharcOpenOptions { PageCacheSize = 1000 });

        // Create non-indexed version (same data, no indexes)
        var noIdxPath = Path.Combine(Path.GetTempPath(), $"sharc_noidx_{Guid.NewGuid()}.db");
        JoinDataGenerator.Generate(noIdxPath, UserCount, 3, createIndexes: false);
        var noIdxBytes = File.ReadAllBytes(noIdxPath);
        _dbNoIndex = SharcDatabase.OpenMemory(noIdxBytes, new SharcOpenOptions { PageCacheSize = 1000 });
        try { File.Delete(noIdxPath); } catch { }

        // SQLite connection (indexed database)
        _sqliteConn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _sqliteConn.Open();

        // Pick a user_id in the middle of the range for representative seeking
        _seekUserId = UserCount / 2;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dbIndexed?.Dispose();
        _dbNoIndex?.Dispose();
        _sqliteConn?.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ── Index-accelerated point lookup: WHERE user_id = N on indexed column ──

    [Benchmark(Baseline = true)]
    public int Sharc_Where_IndexSeek()
    {
        using var reader = _dbIndexed.Query(
            $"SELECT id, user_id, amount FROM orders WHERE user_id = {_seekUserId}");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int Sharc_Where_FullScan()
    {
        using var reader = _dbNoIndex.Query(
            $"SELECT id, user_id, amount FROM orders WHERE user_id = {_seekUserId}");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int SQLite_Where_PointLookup()
    {
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = $"SELECT id, user_id, amount FROM orders WHERE user_id = {_seekUserId}";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    // ── Text key index seek: WHERE dept = 'Engineering' on indexed text column ──

    [Benchmark]
    public int Sharc_WhereText_IndexSeek()
    {
        using var reader = _dbIndexed.Query(
            "SELECT id, name, dept FROM users WHERE dept = 'Engineering'");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int Sharc_WhereText_FullScan()
    {
        using var reader = _dbNoIndex.Query(
            "SELECT id, name, dept FROM users WHERE dept = 'Engineering'");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int SQLite_WhereText_Lookup()
    {
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = "SELECT id, name, dept FROM users WHERE dept = 'Engineering'";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    // ── Unindexed column scan: WHERE status = 'Pending' (no index exists) ──

    [Benchmark]
    public int Sharc_WhereUnindexed_Scan()
    {
        using var reader = _dbIndexed.Query(
            "SELECT id, amount FROM orders WHERE status = 'Pending'");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int SQLite_WhereUnindexed_Scan()
    {
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = "SELECT id, amount FROM orders WHERE status = 'Pending'";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }
}
