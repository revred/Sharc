using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Sharc;
using Sharc.Query;

namespace Sharc.Comparisons;

[MemoryDiagnoser]
public class JoinEfficiencyBenchmarks
{
    private string _dbPath = null!;
    private SharcDatabase _db = null!;

    [Params(1000, 5000)]
    public int UserCount;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_join_{Guid.NewGuid()}.db");
        JoinDataGenerator.Generate(_dbPath, UserCount, 2); // 2 orders per user
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
}
