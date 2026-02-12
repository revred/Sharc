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
/// BenchmarkSpec Category 6: Data Type Decoding.
/// Isolates decode cost from seek cost. Tests per-type overhead:
/// integers, doubles, strings, BLOBs, NULLs — Sharc vs SQLite.
/// </summary>
[BenchmarkCategory("Comparative", "TypeDecode")]
[MemoryDiagnoser]
public class TypeDecodeBenchmarks
{
    private byte[] _dbBytes = null!;
    private SqliteConnection _conn = null!;
    private SqliteCommand _selectEvents = null!;
    private SqliteCommand _selectUsernames = null!;
    private SqliteCommand _selectBios = null!;
    private SqliteCommand _selectAvatars = null!;
    private SqliteCommand _selectIsNull = null!;
    private SqliteCommand _selectAllUsers = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        var dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(dbPath);

        _conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _conn.Open();

        _selectEvents = Prepare("SELECT event_type FROM events");
        _selectUsernames = Prepare("SELECT username FROM users");
        _selectBios = Prepare("SELECT bio FROM users");
        _selectAvatars = Prepare("SELECT avatar FROM users WHERE avatar IS NOT NULL");
        _selectIsNull = Prepare("SELECT bio FROM users");
        _selectAllUsers = Prepare("SELECT id, username, email, bio, age, balance, avatar, is_active, created_at FROM users");
    }

    private SqliteCommand Prepare(string sql)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _selectEvents?.Dispose();
        _selectUsernames?.Dispose();
        _selectBios?.Dispose();
        _selectAvatars?.Dispose();
        _selectIsNull?.Dispose();
        _selectAllUsers?.Dispose();
        _conn?.Dispose();
    }

    // --- 6.1: Decode integers (event_type from events) ---

    [Benchmark]
    [BenchmarkCategory("Integer")]
    public long Sharc_Decode_Integers()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("events", "event_type");
        long sum = 0;
        while (reader.Read())
            sum += reader.GetInt64(0);
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Integer")]
    public long SQLite_Decode_Integers()
    {
        long sum = 0;
        using var reader = _selectEvents.ExecuteReader();
        while (reader.Read())
            sum += reader.GetInt32(0);
        return sum;
    }

    // --- 6.3: Decode doubles (balance from users via events.value) ---

    [Benchmark]
    [BenchmarkCategory("Double")]
    public double Sharc_Decode_Doubles()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "balance");
        double sum = 0;
        while (reader.Read())
            sum += reader.GetDouble(0);
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Double")]
    public double SQLite_Decode_Doubles()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT balance FROM users";
        double sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            sum += reader.GetDouble(0);
        return sum;
    }

    // --- 6.4: Decode short strings (username, 8-20 chars) ---

    [Benchmark]
    [BenchmarkCategory("ShortString")]
    public int Sharc_Decode_ShortStrings()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "username");
        int len = 0;
        while (reader.Read())
            len += reader.GetString(0).Length;
        return len;
    }

    [Benchmark]
    [BenchmarkCategory("ShortString")]
    public int SQLite_Decode_ShortStrings()
    {
        int len = 0;
        using var reader = _selectUsernames.ExecuteReader();
        while (reader.Read())
            len += reader.GetString(0).Length;
        return len;
    }

    // --- 6.5: Decode medium strings (bio, 50-500 chars when not null) ---

    [Benchmark]
    [BenchmarkCategory("MediumString")]
    public int Sharc_Decode_MediumStrings()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "bio");
        int len = 0;
        while (reader.Read())
        {
            if (!reader.IsNull(0))
                len += reader.GetString(0).Length;
        }
        return len;
    }

    [Benchmark]
    [BenchmarkCategory("MediumString")]
    public int SQLite_Decode_MediumStrings()
    {
        int len = 0;
        using var reader = _selectBios.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                len += reader.GetString(0).Length;
        }
        return len;
    }

    // --- 6.7: Decode NULLs (check + skip) ---

    [Benchmark]
    [BenchmarkCategory("NullCheck")]
    public long Sharc_Decode_NullCheck()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users", "bio");
        long nullCount = 0;
        while (reader.Read())
        {
            if (reader.IsNull(0))
                nullCount++;
        }
        return nullCount;
    }

    [Benchmark]
    [BenchmarkCategory("NullCheck")]
    public long SQLite_Decode_NullCheck()
    {
        long nullCount = 0;
        using var reader = _selectIsNull.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
                nullCount++;
        }
        return nullCount;
    }

    // --- 6.8: Decode mixed row (all types, full users row) ---

    [Benchmark]
    [BenchmarkCategory("MixedRow")]
    public long Sharc_Decode_MixedRow_AllTypes()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { PageCacheSize = 0 });
        using var reader = db.CreateReader("users");
        long sum = 0;
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            _ = reader.GetString(2);
            if (!reader.IsNull(3)) _ = reader.GetString(3);
            sum += reader.GetInt64(4);
            sum += (long)reader.GetDouble(5);
            if (!reader.IsNull(6)) _ = reader.GetBlob(6);
            sum += reader.GetInt64(7);
            _ = reader.GetString(8);
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("MixedRow")]
    public long SQLite_Decode_MixedRow_AllTypes()
    {
        long sum = 0;
        using var reader = _selectAllUsers.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            _ = reader.GetString(1);
            _ = reader.GetString(2);
            if (!reader.IsDBNull(3)) _ = reader.GetString(3);
            sum += reader.GetInt32(4);
            sum += (long)reader.GetDouble(5);
            if (!reader.IsDBNull(6)) _ = reader.GetFieldValue<byte[]>(6);
            sum += reader.GetInt32(7);
            _ = reader.GetString(8);
        }
        return sum;
    }
}
