/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Sharc.Benchmarks.Helpers;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// BenchmarkSpec Category 4: Sequential Scan (Full Table).
/// Compares Sharc SharcDatabase/SharcDataReader vs Microsoft.Data.Sqlite
/// for full table scans across different table sizes and column counts.
/// </summary>
[BenchmarkCategory("Comparative", "TableScan")]
[MemoryDiagnoser]
public class TableScanBenchmarks
{
    private string _dbPath = null!;
    private byte[] _dbBytes = null!;
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

    // --- 4.1: Scan 100 rows (config table) ---

    [Benchmark]
    [BenchmarkCategory("Scan100")]
    public long Sharc_Scan100_Config()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("config");
        long count = 0;
        while (reader.Read())
        {
            _ = reader.GetString(0); // key
            _ = reader.GetString(1); // value
            count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Scan100")]
    public long SQLite_Scan100_Config()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM config";
        long count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = reader.GetString(1);
            count++;
        }
        return count;
    }

    // --- 4.2: Scan 10K rows, mixed types (users, all columns) ---

    [Benchmark]
    [BenchmarkCategory("Scan10K")]
    public long Sharc_Scan10K_Users_AllColumns()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);  // id
            _ = reader.GetString(1);     // username
            _ = reader.GetString(2);     // email
            if (!reader.IsNull(3)) _ = reader.GetString(3); // bio
            sum += reader.GetInt64(4);   // age
            _ = reader.GetDouble(5);     // balance
            if (!reader.IsNull(6)) _ = reader.GetBlob(6);   // avatar
            sum += reader.GetInt64(7);   // is_active
            _ = reader.GetString(8);     // created_at
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Scan10K")]
    public long SQLite_Scan10K_Users_AllColumns()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM users";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            _ = reader.GetString(2);
            if (!reader.IsDBNull(3)) _ = reader.GetString(3);
            sum += reader.GetInt32(4);
            _ = reader.GetDouble(5);
            if (!reader.IsDBNull(6)) _ = reader.GetFieldValue<byte[]>(6);
            sum += reader.GetInt32(7);
            _ = reader.GetString(8);
        }
        return sum;
    }

    // --- 4.3: Scan 10K rows, 2 columns only (projection) ---

    [Benchmark]
    [BenchmarkCategory("Scan10K_Projection")]
    public long Sharc_Scan10K_Users_2Columns()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "id", "username");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Scan10K_Projection")]
    public long SQLite_Scan10K_Users_2Columns()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, username FROM users";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
        }
        return sum;
    }

    // --- 4.4: Scan 100K rows, narrow table (events, all columns) ---

    [Benchmark]
    [BenchmarkCategory("Scan100K")]
    public long Sharc_Scan100K_Events_AllColumns()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("events");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);  // id
            sum += reader.GetInt64(1);  // user_id
            sum += reader.GetInt64(2);  // event_type
            sum += reader.GetInt64(3);  // timestamp
            if (!reader.IsNull(4))
                sum += (long)reader.GetDouble(4); // value
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Scan100K")]
    public long SQLite_Scan100K_Events_AllColumns()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM events";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            sum += reader.GetInt32(1);
            sum += reader.GetInt32(2);
            sum += reader.GetInt64(3);
            if (!reader.IsDBNull(4))
                sum += (long)reader.GetDouble(4);
        }
        return sum;
    }

    // --- 4.5: Scan 100K rows, integers only ---

    [Benchmark]
    [BenchmarkCategory("Scan100K_IntOnly")]
    public long Sharc_Scan100K_Events_IntegersOnly()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("events", "id", "event_type");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            sum += reader.GetInt64(1);
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Scan100K_IntOnly")]
    public long SQLite_Scan100K_Events_IntegersOnly()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, event_type FROM events";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            sum += reader.GetInt32(1);
        }
        return sum;
    }

    // --- 4.6: Scan 1K rows, wide table (22 columns) ---

    [Benchmark]
    [BenchmarkCategory("Scan1K_Wide")]
    public long Sharc_Scan1K_Reports_AllColumns()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("reports");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);  // id
            _ = reader.GetString(1);     // label
            for (int c = 2; c <= 21; c++)
            {
                if (!reader.IsNull(c))
                    sum += (long)reader.GetDouble(c);
            }
            if (!reader.IsNull(22)) _ = reader.GetString(22); // notes
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Scan1K_Wide")]
    public long SQLite_Scan1K_Reports_AllColumns()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM reports";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            for (int c = 2; c <= 21; c++)
            {
                if (!reader.IsDBNull(c))
                    sum += (long)reader.GetDouble(c);
            }
            if (!reader.IsDBNull(22)) _ = reader.GetString(22);
        }
        return sum;
    }

    // --- 4.7: Scan with NULLs (count non-null bios) ---

    [Benchmark]
    [BenchmarkCategory("ScanNulls")]
    public long Sharc_ScanNulls_CountNonNullBios()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "bio");
        long count = 0;
        while (reader.Read())
        {
            if (!reader.IsNull(0))
                count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("ScanNulls")]
    public long SQLite_ScanNulls_CountNonNullBios()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE bio IS NOT NULL";
        return (long)cmd.ExecuteScalar()!;
    }
}
