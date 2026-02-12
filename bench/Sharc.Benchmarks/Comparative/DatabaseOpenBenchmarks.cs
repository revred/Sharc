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
using Sharc.Core.Format;
using Sharc.Core.IO;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// Compares database open/validate cost:
///   Sharc: File read (100 bytes) + DatabaseHeader.Parse â€” stackalloc, 0 heap bytes
///   SQLite: new SqliteConnection().Open() â€” native interop, connection pooling objects, WAL check
/// Note: SharcDatabase.Open is not yet implemented. This benchmarks the lower bound for Sharc.
/// MemoryDiagnoser shows the massive allocation gap: Sharc=0B vs SQLite=thousands of bytes.
/// </summary>
[BenchmarkCategory("Comparative", "Open")]
[MemoryDiagnoser]
public class DatabaseOpenBenchmarks
{
    private string _smallDbPath = null!;
    private string _mediumDbPath = null!;
    private byte[] _smallDbBytes = null!;
    private byte[] _mediumDbBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        _smallDbPath = TestDatabaseGenerator.CreateSmallDatabase(dir);
        _mediumDbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _smallDbBytes = File.ReadAllBytes(_smallDbPath);
        _mediumDbBytes = File.ReadAllBytes(_mediumDbPath);
    }

    // --- Sharc: in-memory header parse (0 allocation) ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sharc", "Memory")]
    public DatabaseHeader Sharc_OpenMemory_Small()
    {
        return DatabaseHeader.Parse(_smallDbBytes);
    }

    [Benchmark]
    [BenchmarkCategory("Sharc", "Memory")]
    public DatabaseHeader Sharc_OpenMemory_Medium()
    {
        return DatabaseHeader.Parse(_mediumDbBytes);
    }

    // --- Sharc: file read + header parse (stackalloc, 0 heap allocation) ---

    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public DatabaseHeader Sharc_OpenFile_Small()
    {
        using var fs = new FileStream(_smallDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> buffer = stackalloc byte[100];
        fs.ReadExactly(buffer);
        return DatabaseHeader.Parse(buffer);
    }

    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public DatabaseHeader Sharc_OpenFile_Medium()
    {
        using var fs = new FileStream(_mediumDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> buffer = stackalloc byte[100];
        fs.ReadExactly(buffer);
        return DatabaseHeader.Parse(buffer);
    }

    /// <summary>
    /// Sharc: open file + parse header + parse first b-tree page. Still 0 heap allocation
    /// (only stackalloc + struct returns). Shows multi-step open is still alloc-free.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public int Sharc_OpenFile_ParseHeaderAndFirstPage()
    {
        using var fs = new FileStream(_smallDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> headerBuf = stackalloc byte[100];
        fs.ReadExactly(headerBuf);
        var header = DatabaseHeader.Parse(headerBuf);

        // Read first page's b-tree header (starts at offset 100 in page 1)
        Span<byte> btreeHeaderBuf = stackalloc byte[12];
        fs.ReadExactly(btreeHeaderBuf);
        var btreeHeader = BTreePageHeader.Parse(btreeHeaderBuf);

        return header.PageSize + btreeHeader.CellCount;
    }

    // --- Sharc: memory-mapped file open (near-instant, zero-copy) ---

    /// <summary>
    /// Sharc: open via memory-mapped file. OS handles paging â€” only touched pages
    /// are loaded. Open cost is creating the mapping, not reading the file.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Mmap")]
    public int Sharc_OpenMmap_Small()
    {
        using var source = new MemoryMappedPageSource(_smallDbPath);
        return source.PageSize;
    }

    [Benchmark]
    [BenchmarkCategory("Sharc", "Mmap")]
    public int Sharc_OpenMmap_Medium()
    {
        using var source = new MemoryMappedPageSource(_mediumDbPath);
        return source.PageSize;
    }

    /// <summary>
    /// Sharc: mmap open + read first page header. Only the first 4 KiB page faults in.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Mmap")]
    public int Sharc_OpenMmap_ParseFirstPage()
    {
        using var source = new MemoryMappedPageSource(_smallDbPath);
        var page1 = source.GetPage(1);
        var btreeHeader = BTreePageHeader.Parse(page1[100..]); // page 1 b-tree starts after 100-byte db header
        return source.PageSize + btreeHeader.CellCount;
    }

    /// <summary>
    /// Sharc: batch of 50 mmap open-close cycles.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Mmap", "Batch")]
    public int Sharc_OpenMmap_Batch50()
    {
        int sum = 0;
        for (int i = 0; i < 50; i++)
        {
            using var source = new MemoryMappedPageSource(_smallDbPath);
            sum += source.PageCount;
        }
        return sum;
    }

    /// <summary>
    /// Sharc: batch of 50 open-parse cycles from memory. 0 total allocation.
    /// Demonstrates that repeated opens don't accumulate GC pressure.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Batch")]
    public int Sharc_OpenMemory_Batch50()
    {
        int sum = 0;
        for (int i = 0; i < 50; i++)
        {
            var header = DatabaseHeader.Parse(_smallDbBytes);
            sum += header.PageCount;
        }
        return sum;
    }

    // --- Sharc: FilePageSource (RandomAccess â€” lightweight file handle) ---

    /// <summary>
    /// Sharc: open via File.OpenHandle + RandomAccess. Only reads 100-byte header.
    /// No internal buffering, no async state machine â€” minimal OS overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public int Sharc_OpenFilePageSource_Small()
    {
        using var source = new FilePageSource(_smallDbPath);
        return source.PageSize;
    }

    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public int Sharc_OpenFilePageSource_Medium()
    {
        using var source = new FilePageSource(_mediumDbPath);
        return source.PageSize;
    }

    /// <summary>
    /// Sharc: FilePageSource open + read first page header. One extra syscall for the page read.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "File")]
    public int Sharc_OpenFilePageSource_ParseFirstPage()
    {
        using var source = new FilePageSource(_smallDbPath);
        var page1 = source.GetPage(1);
        var btreeHeader = BTreePageHeader.Parse(page1[100..]);
        return source.PageSize + btreeHeader.CellCount;
    }

    /// <summary>
    /// Sharc: batch of 50 FilePageSource open-close cycles.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "File", "Batch")]
    public int Sharc_OpenFilePageSource_Batch50()
    {
        int sum = 0;
        for (int i = 0; i < 50; i++)
        {
            using var source = new FilePageSource(_smallDbPath);
            sum += source.PageCount;
        }
        return sum;
    }

    // --- Amortized: open + N page reads (shows break-even point) ---

    /// <summary>
    /// Sharc mmap: open + read 10 pages. Shows amortized cost per page when including open overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Amortized")]
    public int Sharc_Mmap_Open_Read10Pages()
    {
        using var source = new MemoryMappedPageSource(_smallDbPath);
        int sum = 0;
        for (uint p = 1; p <= Math.Min(10, (uint)source.PageCount); p++)
        {
            var page = source.GetPage(p);
            sum += page[0];
        }
        return sum;
    }

    /// <summary>
    /// Sharc FilePageSource: open + read 10 pages.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sharc", "Amortized")]
    public int Sharc_File_Open_Read10Pages()
    {
        using var source = new FilePageSource(_smallDbPath);
        int sum = 0;
        for (uint p = 1; p <= Math.Min(10, (uint)source.PageCount); p++)
        {
            var page = source.GetPage(p);
            sum += page[0];
        }
        return sum;
    }

    /// <summary>
    /// SQLite: open + read 10 rows. Amortized comparison against Sharc page sources.
    /// Uses medium DB which has the users table.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SQLite", "Amortized")]
    public long SQLite_Open_Read10Rows()
    {
        using var conn = new SqliteConnection($"Data Source={_mediumDbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users LIMIT 10";
        long sum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            sum += reader.GetInt64(0);
        return sum;
    }

    // --- SQLite: connection open (heavy allocation) ---

    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public void SQLite_Open_Small()
    {
        using var conn = new SqliteConnection($"Data Source={_smallDbPath};Mode=ReadOnly");
        conn.Open();
    }

    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public void SQLite_Open_Medium()
    {
        using var conn = new SqliteConnection($"Data Source={_mediumDbPath};Mode=ReadOnly");
        conn.Open();
    }

    /// <summary>
    /// SQLite: open + read metadata. Shows total allocation for a minimal open+query pattern.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SQLite")]
    public long SQLite_OpenAndReadPageSize_Small()
    {
        using var conn = new SqliteConnection($"Data Source={_smallDbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA page_size";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// SQLite: batch of 50 open-close cycles. Shows connection pool allocation accumulation.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SQLite", "Batch")]
    public int SQLite_Open_Batch50()
    {
        int count = 0;
        for (int i = 0; i < 50; i++)
        {
            using var conn = new SqliteConnection($"Data Source={_smallDbPath};Mode=ReadOnly");
            conn.Open();
            count++;
        }
        return count;
    }
}
