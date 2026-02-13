/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;

namespace Sharc.Comparisons;

/// <summary>
/// Graph scan benchmarks: full table scans over concept (node) and relation (edge) tables.
/// Compares Sharc sequential scan vs SQLite SELECT for graph-shaped data.
/// Database: 5K nodes, 15K edges (code dependency graph topology).
/// </summary>
[BenchmarkCategory("Comparative", "GraphScan")]
[MemoryDiagnoser]
public class GraphScanBenchmarks
{
    private string _dbPath = null!;
    private byte[] _dbBytes = null!;
    private SqliteConnection _conn = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_graph_bench");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "graph_scan_bench.db");

        if (!File.Exists(_dbPath))
            GraphGenerator.GenerateSQLite(_dbPath, nodeCount: 5000, edgeCount: 15000);
        _dbBytes = File.ReadAllBytes(_dbPath);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _conn?.Dispose();
    }

    // --- Scan all nodes (concepts) ---

    [Benchmark]
    [BenchmarkCategory("NodeScan")]
    public long Sharc_ScanAllNodes()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("_concepts");
        long count = 0;
        while (reader.Read())
        {
            _ = reader.GetString(0);  // id
            _ = reader.GetInt64(1);   // key
            _ = reader.GetInt64(2);   // type_id
            _ = reader.GetString(3);  // data
            count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("NodeScan")]
    public long SQLite_ScanAllNodes()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, key, type_id, data FROM _concepts";
        long count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = reader.GetInt64(1);
            _ = reader.GetInt64(2);
            _ = reader.GetString(3);
            count++;
        }
        return count;
    }

    // --- Scan all edges (relations) ---

    [Benchmark]
    [BenchmarkCategory("EdgeScan")]
    public long Sharc_ScanAllEdges()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("_relations");
        long count = 0;
        while (reader.Read())
        {
            _ = reader.GetString(0);  // id
            _ = reader.GetInt64(1);   // source_key
            _ = reader.GetInt64(2);   // kind
            _ = reader.GetInt64(3);   // target_key
            count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("EdgeScan")]
    public long SQLite_ScanAllEdges()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, source_key, kind, target_key FROM _relations";
        long count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = reader.GetInt64(1);
            _ = reader.GetInt64(2);
            _ = reader.GetInt64(3);
            count++;
        }
        return count;
    }

    // --- Scan nodes with projection (id + type_id only) ---

    [Benchmark]
    [BenchmarkCategory("NodeProjection")]
    public long Sharc_ScanNodes_Projection()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("_concepts", "id", "type_id");
        long count = 0;
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = reader.GetInt64(1);
            count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("NodeProjection")]
    public long SQLite_ScanNodes_Projection()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, type_id FROM _concepts";
        long count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = reader.GetInt64(1);
            count++;
        }
        return count;
    }

    // --- Scan edges and count by kind (filter simulation) ---

    [Benchmark]
    [BenchmarkCategory("EdgeFilter")]
    public long Sharc_ScanEdges_CountByKind()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("_relations");
        long matchCount = 0;
        while (reader.Read())
        {
            long kind = reader.GetInt64(2);
            if (kind == 15) // "Calls" relationship
                matchCount++;
        }
        return matchCount;
    }

    [Benchmark]
    [BenchmarkCategory("EdgeFilter")]
    public long SQLite_ScanEdges_CountByKind()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT kind FROM _relations";
        long matchCount = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long kind = reader.GetInt64(0);
            if (kind == 15)
                matchCount++;
        }
        return matchCount;
    }
}
