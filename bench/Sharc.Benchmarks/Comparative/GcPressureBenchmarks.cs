/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Sharc.Benchmarks.Helpers;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// BenchmarkSpec Category 9: Memory and GC Pressure Under Load.
/// Sustained load reveals GC impact. A library that allocates 0 B per read
/// across 100K reads means zero GC pauses. This is the "so what?" for allocation numbers.
/// </summary>
[BenchmarkCategory("Comparative", "GcPressure")]
[MemoryDiagnoser]
public class GcPressureBenchmarks
{
    private byte[] _dbBytes = null!;
    private string _dbPath = null!;
    private SqliteConnection _conn = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        _dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(_dbPath);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _conn?.Dispose();
    }

    // --- 9.1: Sustained scan â€” scan events (100K rows) Ã— 3 iterations ---

    [Benchmark]
    [BenchmarkCategory("SustainedScan")]
    public long Sharc_SustainedScan_Events_3x()
    {
        long sum = 0;
        for (int iter = 0; iter < 3; iter++)
        {
            using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
            using var reader = db.CreateReader("events");
            while (reader.Read())
            {
                sum += reader.GetInt64(0);
                sum += reader.GetInt64(2);
            }
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("SustainedScan")]
    public long SQLite_SustainedScan_Events_3x()
    {
        long sum = 0;
        for (int iter = 0; iter < 3; iter++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, event_type FROM events";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sum += reader.GetInt64(0);
                sum += reader.GetInt32(1);
            }
        }
        return sum;
    }

    // --- 9.2: Sustained scan with strings â€” users (10K rows) Ã— 5 iterations ---

    [Benchmark]
    [BenchmarkCategory("SustainedStringScan")]
    public int Sharc_SustainedStringScan_Users_5x()
    {
        int totalLen = 0;
        for (int iter = 0; iter < 5; iter++)
        {
            using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
            using var reader = db.CreateReader("users", "username", "email");
            while (reader.Read())
            {
                totalLen += reader.GetString(0).Length;
                totalLen += reader.GetString(1).Length;
            }
        }
        return totalLen;
    }

    [Benchmark]
    [BenchmarkCategory("SustainedStringScan")]
    public int SQLite_SustainedStringScan_Users_5x()
    {
        int totalLen = 0;
        for (int iter = 0; iter < 5; iter++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT username, email FROM users";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                totalLen += reader.GetString(0).Length;
                totalLen += reader.GetString(1).Length;
            }
        }
        return totalLen;
    }

    // --- 9.3: Peak working set â€” full scan of all tables in sequence ---

    [Benchmark]
    [BenchmarkCategory("PeakMemory")]
    public long Sharc_ScanAllTables()
    {
        long totalRows = 0;
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        foreach (var table in db.Schema.Tables)
        {
            using var reader = db.CreateReader(table.Name);
            while (reader.Read())
                totalRows++;
        }
        return totalRows;
    }

    [Benchmark]
    [BenchmarkCategory("PeakMemory")]
    public long SQLite_ScanAllTables()
    {
        long totalRows = 0;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var tables = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            using var scanCmd = _conn.CreateCommand();
            scanCmd.CommandText = $"SELECT * FROM [{table}]";
            using var reader = scanCmd.ExecuteReader();
            while (reader.Read())
                totalRows++;
        }
        return totalRows;
    }

    // --- 9.4: Integer-only sustained scan (best case for zero-alloc) ---

    [Benchmark]
    [BenchmarkCategory("SustainedIntScan")]
    public long Sharc_SustainedIntScan_10x()
    {
        long sum = 0;
        for (int iter = 0; iter < 10; iter++)
        {
            using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
            using var reader = db.CreateReader("events", "id", "event_type", "timestamp");
            while (reader.Read())
            {
                sum += reader.GetInt64(0);
                sum += reader.GetInt64(1);
                sum += reader.GetInt64(2);
            }
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("SustainedIntScan")]
    public long SQLite_SustainedIntScan_10x()
    {
        long sum = 0;
        for (int iter = 0; iter < 10; iter++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, event_type, timestamp FROM events";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sum += reader.GetInt64(0);
                sum += reader.GetInt32(1);
                sum += reader.GetInt64(2);
            }
        }
        return sum;
    }
}
