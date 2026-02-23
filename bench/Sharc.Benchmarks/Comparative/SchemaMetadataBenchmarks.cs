/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Sharc.Benchmarks.Helpers;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// BenchmarkSpec Category 2: Schema and Metadata Introspection.
/// Sharc's direct struct parsing vs SQLite's PRAGMA-through-SQL overhead.
/// This is Sharc's strongest category.
/// </summary>
[BenchmarkCategory("Comparative", "Schema")]
[MemoryDiagnoser]
public class SchemaMetadataBenchmarks
{
    private byte[] _dbBytes = null!;
    private SqliteConnection _conn = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        var dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(dbPath);

        _conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _conn.Open();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        SqliteConnection.ClearAllPools();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _conn?.Dispose();
    }

    // --- 2.3: List all table names ---

    [Benchmark]
    [BenchmarkCategory("ListTables")]
    public int Sharc_ListAllTableNames()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        return db.Schema.Tables.Count;
    }

    [Benchmark]
    [BenchmarkCategory("ListTables")]
    public int SQLite_ListAllTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        int count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _ = reader.GetString(0);
            count++;
        }
        return count;
    }

    // --- 2.4: Get column info for 1 table ---

    [Benchmark]
    [BenchmarkCategory("ColumnInfo")]
    public int Sharc_GetColumnInfo_Users()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        var table = db.Schema.GetTable("users");
        return table.Columns.Count;
    }

    [Benchmark]
    [BenchmarkCategory("ColumnInfo")]
    public int SQLite_GetColumnInfo_Users()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('users')";
        int count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            count++;
        return count;
    }

    // --- 2.5: Get column info for ALL tables ---

    [Benchmark]
    [BenchmarkCategory("AllColumnInfo")]
    public int Sharc_GetColumnInfo_AllTables()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        int totalCols = 0;
        foreach (var table in db.Schema.Tables)
            totalCols += table.Columns.Count;
        return totalCols;
    }

    [Benchmark]
    [BenchmarkCategory("AllColumnInfo")]
    public int SQLite_GetColumnInfo_AllTables()
    {
        // First get all table names
        var tables = new List<string>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        int totalCols = 0;
        foreach (var table in tables)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}')";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                totalCols++;
        }
        return totalCols;
    }

    // --- 2.6: Batch 100 schema reads ---

    [Benchmark]
    [BenchmarkCategory("BatchSchema")]
    public int Sharc_BatchSchemaRead_100x()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
            sum += db.Schema.Tables.Count;
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("BatchSchema")]
    public int SQLite_BatchSchemaRead_100x()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _ = reader.GetString(0);
                sum++;
            }
        }
        return sum;
    }
}
