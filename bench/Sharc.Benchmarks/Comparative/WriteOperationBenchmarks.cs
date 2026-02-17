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
using Sharc.Core;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// Sharc vs SQLite: Delete and Update operations.
/// Both operate on identical SQLite format 3 files — same B-tree page layout,
/// same record encoding. The difference is the write path:
/// Sharc uses direct B-tree page manipulation; SQLite goes through SQL parse + optimize + VM.
/// </summary>
[BenchmarkCategory("Comparative", "WriteOps")]
[MemoryDiagnoser]
public class WriteOperationBenchmarks
{
    private string _dbPath = null!;
    private byte[] _dbBytes = null!;

    // Pre-built ColumnValue arrays — avoids allocation in the benchmark hot path.
    // Events table: user_id INTEGER, event_type INTEGER, timestamp INTEGER, value REAL
    private static readonly ColumnValue[] UpdateValues =
    [
        ColumnValue.FromInt64(1, 42),          // user_id   (serial type 1 = 1-byte int)
        ColumnValue.FromInt64(1, 99),          // event_type
        ColumnValue.FromInt64(4, 1700000000),  // timestamp (serial type 4 = 4-byte int)
        ColumnValue.FromDouble(123.45),        // value     (serial type 7 = IEEE 754)
    ];

    private static readonly ColumnValue[] BatchUpdateValues =
    [
        ColumnValue.FromInt64(1, 42),
        ColumnValue.FromInt64(1, 99),
        ColumnValue.FromInt64(4, 1700000000),
        ColumnValue.FromDouble(99.99),
    ];

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench_write");
        Directory.CreateDirectory(dir);
        _dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(_dbPath);
    }

    [IterationSetup]
    public void RestoreDatabase()
    {
        // Release any pooled SQLite connections so the file handle is free.
        SqliteConnection.ClearAllPools();
        // Restore a fresh database so each iteration measures the actual
        // mutation path (row found → deleted/updated), not the miss path.
        File.WriteAllBytes(_dbPath, _dbBytes);
    }

    // --- DELETE: Single row ---

    [Benchmark]
    [BenchmarkCategory("Delete")]
    public bool Sharc_Delete_SingleRow()
    {
        using var writer = SharcWriter.Open(_dbPath);
        return writer.Delete("events", 500);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Delete")]
    public int SQLite_Delete_SingleRow()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM events WHERE rowid = 500";
        return cmd.ExecuteNonQuery();
    }

    // --- DELETE: Batch 100 rows ---

    [Benchmark]
    [BenchmarkCategory("DeleteBatch")]
    public int Sharc_Delete_100Rows()
    {
        using var writer = SharcWriter.Open(_dbPath);
        int count = 0;
        using var tx = writer.BeginTransaction();
        for (long rowId = 1000; rowId < 1100; rowId++)
        {
            if (tx.Delete("events", rowId))
                count++;
        }
        tx.Commit();
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("DeleteBatch")]
    public int SQLite_Delete_100Rows()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM events WHERE rowid = @id";
        var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
        int count = 0;
        for (long rowId = 1000; rowId < 1100; rowId++)
        {
            pId.Value = rowId;
            count += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return count;
    }

    // --- UPDATE: Single row ---

    [Benchmark]
    [BenchmarkCategory("Update")]
    public bool Sharc_Update_SingleRow()
    {
        using var writer = SharcWriter.Open(_dbPath);
        return writer.Update("events", 500, UpdateValues);
    }

    [Benchmark]
    [BenchmarkCategory("Update")]
    public int SQLite_Update_SingleRow()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET user_id=42, event_type=99, timestamp=1700000000, value=123.45 WHERE rowid = 500";
        return cmd.ExecuteNonQuery();
    }

    // --- UPDATE: Batch 100 rows ---

    [Benchmark]
    [BenchmarkCategory("UpdateBatch")]
    public int Sharc_Update_100Rows()
    {
        using var writer = SharcWriter.Open(_dbPath);
        int count = 0;
        using var tx = writer.BeginTransaction();
        for (long rowId = 2000; rowId < 2100; rowId++)
        {
            if (tx.Update("events", rowId, BatchUpdateValues))
                count++;
        }
        tx.Commit();
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("UpdateBatch")]
    public int SQLite_Update_100Rows()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE events SET user_id=42, event_type=99, timestamp=1700000000, value=99.99 WHERE rowid = @id";
        var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
        int count = 0;
        for (long rowId = 2000; rowId < 2100; rowId++)
        {
            pId.Value = rowId;
            count += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return count;
    }
}
