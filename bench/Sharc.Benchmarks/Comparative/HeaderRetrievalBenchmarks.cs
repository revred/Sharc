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
using Sharc.Core.Format;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// Compares header/metadata retrieval:
///   Sharc: DatabaseHeader.Parse() from raw bytes (pure C#, zero SQL)
///   SQLite: PRAGMA queries via Microsoft.Data.Sqlite (C library + SQL VM)
/// Expected: Sharc 50-200x faster, 0 bytes allocated vs hundreds for SQLite.
/// </summary>
[BenchmarkCategory("Comparative", "Header")]
[MemoryDiagnoser]
public class HeaderRetrievalBenchmarks
{
    private string _dbPath = null!;
    private byte[] _dbBytes = null!;
    private SqliteConnection _conn = null!;
    private SqliteCommand _pragmaPageSize = null!;
    private SqliteCommand _pragmaPageCount = null!;
    private SqliteCommand _pragmaEncoding = null!;
    private SqliteCommand _pragmaUserVersion = null!;
    private SqliteCommand _pragmaApplicationId = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        _dbPath = TestDatabaseGenerator.CreateSmallDatabase(dir);
        _dbBytes = File.ReadAllBytes(_dbPath);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();

        _pragmaPageSize = CreatePragma("PRAGMA page_size");
        _pragmaPageCount = CreatePragma("PRAGMA page_count");
        _pragmaEncoding = CreatePragma("PRAGMA encoding");
        _pragmaUserVersion = CreatePragma("PRAGMA user_version");
        _pragmaApplicationId = CreatePragma("PRAGMA application_id");
    }

    private SqliteCommand CreatePragma(string sql)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pragmaPageSize?.Dispose();
        _pragmaPageCount?.Dispose();
        _pragmaEncoding?.Dispose();
        _pragmaUserVersion?.Dispose();
        _pragmaApplicationId?.Dispose();
        _conn?.Dispose();
    }

    // --- Sharc: direct byte-level parsing (0 allocation) ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sharc")]
    public DatabaseHeader Sharc_ParseFullHeader()
    {
        return DatabaseHeader.Parse(_dbBytes);
    }

    [Benchmark]
    [BenchmarkCategory("Sharc")]
    public int Sharc_GetPageSize()
    {
        var header = DatabaseHeader.Parse(_dbBytes);
        return header.PageSize;
    }

    [Benchmark]
    [BenchmarkCategory("Sharc")]
    public int Sharc_GetAllMetadata()
    {
        var header = DatabaseHeader.Parse(_dbBytes);
        return header.PageSize + header.PageCount + header.TextEncoding
             + header.UserVersion + header.ApplicationId;
    }

    [Benchmark]
    [BenchmarkCategory("Sharc")]
    public bool Sharc_HasValidMagic()
    {
        return DatabaseHeader.HasValidMagic(_dbBytes);
    }

    /// <summary>
    /// Sharc: parse header 100 times from in-memory buffer.
    /// Shows zero-alloc advantage scales linearly â€” 0 bytes total regardless of iteration count.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Batch")]
    public int Sharc_ParseHeader_Batch100()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var header = DatabaseHeader.Parse(_dbBytes);
            sum += header.PageSize;
        }
        return sum;
    }

    // --- SQLite: PRAGMA queries through native C library (allocates per call) ---

    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public long SQLite_GetPageSize()
    {
        return (long)_pragmaPageSize.ExecuteScalar()!;
    }

    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public long SQLite_GetPageCount()
    {
        return (long)_pragmaPageCount.ExecuteScalar()!;
    }

    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public long SQLite_GetAllMetadata()
    {
        var ps = (long)_pragmaPageSize.ExecuteScalar()!;
        var pc = (long)_pragmaPageCount.ExecuteScalar()!;
        var enc = _pragmaEncoding.ExecuteScalar()!;
        var uv = (long)_pragmaUserVersion.ExecuteScalar()!;
        var aid = (long)_pragmaApplicationId.ExecuteScalar()!;
        return ps + pc + uv + aid;
    }

    /// <summary>
    /// SQLite: 100 PRAGMA calls. Shows per-call allocation pressure from interop + boxing.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SQLite", "Batch")]
    public long SQLite_GetPageSize_Batch100()
    {
        long sum = 0;
        for (int i = 0; i < 100; i++)
        {
            sum += (long)_pragmaPageSize.ExecuteScalar()!;
        }
        return sum;
    }
}
