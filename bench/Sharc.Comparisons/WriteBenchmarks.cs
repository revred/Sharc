// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Sharc.Core;

namespace Sharc.Comparisons;

/// <summary>
/// Write benchmarks comparing Sharc INSERT vs SQLite INSERT.
/// Tests single-row, batch, and transactional insert patterns.
///
/// Each benchmark creates a fresh database with schema, then inserts rows.
/// This measures the full write path: record encoding → B-tree mutation → journal → commit.
///
/// Dataset: "logs" table with 4 columns (id INTEGER, message TEXT, level INTEGER, timestamp INTEGER).
/// </summary>
[BenchmarkCategory("Comparative", "Write")]
[MemoryDiagnoser]
public class WriteBenchmarks
{
    private string _benchDir = null!;

    // Pre-built column values for Sharc (reused across iterations to isolate insert cost)
    private ColumnValue[][] _rows100 = null!;
    private ColumnValue[][] _rows1000 = null!;
    private ColumnValue[][] _rows10000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _benchDir = Path.Combine(Path.GetTempPath(), "sharc_write_bench");
        Directory.CreateDirectory(_benchDir);

        // Pre-build ColumnValue rows to isolate encode+insert cost from data generation
        var rng = new Random(42);
        _rows100 = BuildRows(100, rng);
        _rows1000 = BuildRows(1000, rng);
        _rows10000 = BuildRows(10000, rng);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_benchDir))
        {
            try { Directory.Delete(_benchDir, true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  1. SINGLE ROW INSERT — one row, auto-commit
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("SingleInsert")]
    public long Sharc_Insert_1Row()
    {
        var dbPath = CreateEmptyDatabase("sharc_1row");
        using var writer = SharcWriter.Open(dbPath);
        return writer.Insert("logs", _rows100[0]);
    }

    [Benchmark]
    [BenchmarkCategory("SingleInsert")]
    public long SQLite_Insert_1Row()
    {
        var dbPath = CreateEmptyDatabase("sqlite_1row");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO logs (message, level, timestamp) VALUES ($msg, $lvl, $ts)";
        cmd.Parameters.AddWithValue("$msg", "Log entry 0");
        cmd.Parameters.AddWithValue("$lvl", 0 % 5);
        cmd.Parameters.AddWithValue("$ts", 1700000000L);
        return cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. BATCH INSERT 100 ROWS — single transaction
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int Sharc_Insert_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sharc_100");
        using var writer = SharcWriter.Open(dbPath);
        var rowIds = writer.InsertBatch("logs", _rows100);
        return rowIds.Length;
    }

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int SQLite_Insert_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sqlite_100");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return SqliteInsertN(conn, 100);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. BATCH INSERT 1,000 ROWS — single transaction
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int Sharc_Insert_1000Rows()
    {
        var dbPath = CreateEmptyDatabase("sharc_1k");
        using var writer = SharcWriter.Open(dbPath);
        var rowIds = writer.InsertBatch("logs", _rows1000);
        return rowIds.Length;
    }

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int SQLite_Insert_1000Rows()
    {
        var dbPath = CreateEmptyDatabase("sqlite_1k");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return SqliteInsertN(conn, 1000);
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. BATCH INSERT 10,000 ROWS — single transaction, stress test
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int Sharc_Insert_10000Rows()
    {
        var dbPath = CreateEmptyDatabase("sharc_10k");
        using var writer = SharcWriter.Open(dbPath);
        var rowIds = writer.InsertBatch("logs", _rows10000);
        return rowIds.Length;
    }

    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int SQLite_Insert_10000Rows()
    {
        var dbPath = CreateEmptyDatabase("sqlite_10k");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return SqliteInsertN(conn, 10000);
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. EXPLICIT TRANSACTION — 100 inserts with BeginTransaction/Commit
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("Transaction")]
    public int Sharc_Transaction_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sharc_tx100");
        using var writer = SharcWriter.Open(dbPath);
        using var tx = writer.BeginTransaction();
        for (int i = 0; i < 100; i++)
        {
            tx.Insert("logs", _rows100[i]);
        }
        tx.Commit();
        return 100;
    }

    [Benchmark]
    [BenchmarkCategory("Transaction")]
    public int SQLite_Transaction_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sqlite_tx100");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return SqliteInsertN(conn, 100);
    }

    // ═══════════════════════════════════════════════════════════════
    //  6. INSERT + READ-BACK — write 100 rows, then scan to verify
    // ═══════════════════════════════════════════════════════════════

    [Benchmark]
    [BenchmarkCategory("InsertReadBack")]
    public int Sharc_InsertAndRead_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sharc_rw100");
        using var writer = SharcWriter.Open(dbPath);
        writer.InsertBatch("logs", _rows100);

        // Re-open for read (fresh reader, no stale pages)
        using var db = SharcDatabase.Open(dbPath);
        using var reader = db.CreateReader("logs");
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("InsertReadBack")]
    public int SQLite_InsertAndRead_100Rows()
    {
        var dbPath = CreateEmptyDatabase("sqlite_rw100");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        SqliteInsertN(conn, 100);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT message, level, timestamp FROM logs";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private string CreateEmptyDatabase(string name)
    {
        var dbPath = Path.Combine(_benchDir, $"{name}_{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=false");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = DELETE;
            PRAGMA page_size = 4096;
            CREATE TABLE logs (
                id INTEGER PRIMARY KEY,
                message TEXT NOT NULL,
                level INTEGER NOT NULL,
                timestamp INTEGER NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
        return dbPath;
    }

    private static ColumnValue[][] BuildRows(int count, Random rng)
    {
        var rows = new ColumnValue[count][];
        for (int i = 0; i < count; i++)
        {
            var msg = $"Log entry {i}: operation completed successfully";
            var msgBytes = Encoding.UTF8.GetBytes(msg);
            rows[i] =
            [
                ColumnValue.Text(msgBytes.Length * 2 + 13, msgBytes),
                ColumnValue.FromInt64(1, i % 5),
                ColumnValue.FromInt64(4, 1700000000L + i),
            ];
        }
        return rows;
    }

    private static int SqliteInsertN(SqliteConnection conn, int count)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO logs (message, level, timestamp) VALUES ($msg, $lvl, $ts)";
        var pMsg = cmd.Parameters.Add("$msg", SqliteType.Text);
        var pLvl = cmd.Parameters.Add("$lvl", SqliteType.Integer);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);

        for (int i = 0; i < count; i++)
        {
            pMsg.Value = $"Log entry {i}: operation completed successfully";
            pLvl.Value = i % 5;
            pTs.Value = 1700000000L + i;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return count;
    }
}
