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
/// BenchmarkSpec Category 8: Realistic Application Workloads.
/// Composite benchmarks simulating what real applications do.
/// These are the benchmarks that tell the real story.
/// </summary>
[BenchmarkCategory("Comparative", "Realistic")]
[MemoryDiagnoser]
public class RealisticWorkloadBenchmarks
{
    private byte[] _dbBytes = null!;
    private string _dbPath = null!;
    private SqliteConnection _conn = null!;
    private SqliteCommand _selectUserById = null!;
    private SqliteCommand _selectAllTableNames = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        _dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(_dbPath);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();

        _selectUserById = _conn.CreateCommand();
        _selectUserById.CommandText = "SELECT * FROM users WHERE id = @id";
        _selectUserById.Parameters.Add("@id", SqliteType.Integer);

        _selectAllTableNames = _conn.CreateCommand();
        _selectAllTableNames.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _selectUserById?.Dispose();
        _selectAllTableNames?.Dispose();
        _conn?.Dispose();
    }

    // --- 8.1: "Load user profile" â€” single user lookup via B-tree Seek ---

    [Benchmark]
    [BenchmarkCategory("LoadProfile")]
    public long Sharc_LoadUserProfile()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users");
        // B-tree Seek: binary search descent to rowid 5000
        if (reader.Seek(5000))
        {
            _ = reader.GetString(1); // username
            _ = reader.GetString(2); // email
            if (!reader.IsNull(3)) _ = reader.GetString(3); // bio
            return reader.GetInt64(4); // age
        }
        return -1;
    }

    [Benchmark]
    [BenchmarkCategory("LoadProfile")]
    public long SQLite_LoadUserProfile()
    {
        _selectUserById.Parameters[0].Value = 5000L;
        using var reader = _selectUserById.ExecuteReader();
        if (reader.Read())
        {
            _ = reader.GetString(1);
            _ = reader.GetString(2);
            if (!reader.IsDBNull(3)) _ = reader.GetString(3);
            return reader.GetInt32(4);
        }
        return -1;
    }

    // --- 8.4: "Export users to CSV" â€” full table scan with string formatting ---

    [Benchmark]
    [BenchmarkCategory("ExportCSV")]
    public int Sharc_ExportUsersCSV()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users");
        int totalLen = 0;
        while (reader.Read())
        {
            // Simulate CSV formatting
            var line = string.Format("{0},{1},{2},{3},{4}",
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(4),
                reader.GetDouble(5));
            totalLen += line.Length;
        }
        return totalLen;
    }

    [Benchmark]
    [BenchmarkCategory("ExportCSV")]
    public int SQLite_ExportUsersCSV()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, email, age, balance FROM users";
        int totalLen = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var line = string.Format("{0},{1},{2},{3},{4}",
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDouble(4));
            totalLen += line.Length;
        }
        return totalLen;
    }

    // --- 8.6: "Schema migration check" â€” read all tables, columns, indexes ---

    [Benchmark]
    [BenchmarkCategory("SchemaCheck")]
    public int Sharc_SchemaMigrationCheck()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        int totalColumns = 0;
        foreach (var table in db.Schema.Tables)
            totalColumns += table.Columns.Count;
        totalColumns += db.Schema.Indexes.Count;
        totalColumns += db.Schema.Views.Count;
        return totalColumns;
    }

    [Benchmark]
    [BenchmarkCategory("SchemaCheck")]
    public int SQLite_SchemaMigrationCheck()
    {
        int totalColumns = 0;

        // Get all table names
        var tableNames = new List<string>();
        using (var reader = _selectAllTableNames.ExecuteReader())
        {
            while (reader.Read())
                tableNames.Add(reader.GetString(0));
        }

        // Get column info for each table
        foreach (var table in tableNames)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}')";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                totalColumns++;
        }

        // Count indexes
        using var idxCmd = _conn.CreateCommand();
        idxCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index'";
        totalColumns += (int)(long)idxCmd.ExecuteScalar()!;

        return totalColumns;
    }

    // --- 8.7: "Batch read" â€” first 500 users with column projection ---

    [Benchmark]
    [BenchmarkCategory("BatchLookup")]
    public long Sharc_BatchLookup_500Users()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        long sum = 0;
        using var reader = db.CreateReader("users", "id", "username", "age");
        int count = 0;
        while (reader.Read() && count < 500)
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            sum += reader.GetInt64(2);
            count++;
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("BatchLookup")]
    public long SQLite_BatchLookup_500Users()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, age FROM users LIMIT 500";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            sum += reader.GetInt32(2);
        }
        return sum;
    }

    // --- 8.8: "Config read" â€” 10 config key lookups ---

    [Benchmark]
    [BenchmarkCategory("ConfigRead")]
    public int Sharc_ConfigRead_10Keys()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        int totalLen = 0;
        using var reader = db.CreateReader("config");
        int found = 0;
        while (reader.Read() && found < 10)
        {
            totalLen += reader.GetString(1).Length;
            found++;
        }
        return totalLen;
    }

    [Benchmark]
    [BenchmarkCategory("ConfigRead")]
    public int SQLite_ConfigRead_10Keys()
    {
        int totalLen = 0;
        for (int i = 0; i < 10; i++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM config WHERE key = @k";
            cmd.Parameters.AddWithValue("@k", $"config_key_{i:D3}");
            var val = cmd.ExecuteScalar();
            if (val is string s) totalLen += s.Length;
        }
        return totalLen;
    }

    // --- Open + Read 1 row + Close (BenchmarkSpec 1.5) ---

    [Benchmark]
    [BenchmarkCategory("OpenReadClose")]
    public long Sharc_OpenRead1RowClose()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users");
        reader.Read();
        return reader.GetInt64(0);
    }

    [Benchmark]
    [BenchmarkCategory("OpenReadClose")]
    public long SQLite_OpenRead1RowClose()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users LIMIT 1";
        return (long)cmd.ExecuteScalar()!;
    }
}
