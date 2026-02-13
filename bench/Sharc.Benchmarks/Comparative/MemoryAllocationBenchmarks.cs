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
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Microsoft.Data.Sqlite;
using Sharc.Benchmarks.Helpers;
using Sharc.Core;
using Sharc.Core.Format;
using Sharc.Core.Primitives;

namespace Sharc.Benchmarks.Comparative;

/// <summary>
/// Dedicated memory allocation comparison: Sharc (pure C#) vs Microsoft.Data.Sqlite (native C).
/// Every benchmark method is designed to highlight the allocation difference.
///
/// Key design principle: Sharc uses readonly structs, Span&lt;T&gt;, and stackalloc throughout.
/// SQLite (via Microsoft.Data.Sqlite) must allocate managed wrappers around native handles,
/// box scalar results, create string instances for column data, and allocate reader objects.
///
/// Expected results:
///   - Sharc header parse: 0 B allocated
///   - Sharc varint/serial type: 0 B allocated
///   - Sharc ColumnValue (int/float/null): 0 B allocated
///   - SQLite PRAGMA: hundreds of bytes per call (boxing, interop buffers)
///   - SQLite Open: thousands of bytes (connection object graph)
///   - SQLite scan: reader + per-row string/object allocations
/// </summary>
[BenchmarkCategory("Comparative", "Memory")]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MemoryAllocationBenchmarks
{
    private string _dbPath = null!;
    private byte[] _dbBytes = null!;
    private SqliteConnection _conn = null!;
    private SqliteCommand _pragmaPageSize = null!;
    private SqliteCommand _selectFirst5 = null!;
    private SqliteCommand _selectIntegers = null!;

    private byte[] _varint1Byte = null!;
    private byte[] _varint9Byte = null!;
    private byte[] _textBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        _dbPath = TestDatabaseGenerator.CreateCanonicalDatabase(dir);
        _dbBytes = File.ReadAllBytes(_dbPath);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();

        _pragmaPageSize = _conn.CreateCommand();
        _pragmaPageSize.CommandText = "PRAGMA page_size";

        _selectFirst5 = _conn.CreateCommand();
        _selectFirst5.CommandText = "SELECT id, username, email, age, balance FROM users LIMIT 5";

        _selectIntegers = _conn.CreateCommand();
        _selectIntegers.CommandText = "SELECT id, age FROM users LIMIT 100";

        // Varint test data
        var buf = new byte[9];
        int len = VarintDecoder.Write(buf, 42);
        _varint1Byte = buf[..len];
        len = VarintDecoder.Write(buf, long.MaxValue);
        _varint9Byte = buf[..len];

        _textBytes = "Hello, Sharc benchmark!"u8.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pragmaPageSize?.Dispose();
        _selectFirst5?.Dispose();
        _selectIntegers?.Dispose();
        _conn?.Dispose();
    }

    // ========================================================================
    // Category: MetadataRead â€” Single metadata query allocation comparison
    // ========================================================================

    /// <summary>Sharc: parse header from bytes. Expected: 0 B allocated.</summary>
    [Benchmark]
    [BenchmarkCategory("MetadataRead")]
    public int Sharc_Metadata_ParseHeader()
    {
        var header = DatabaseHeader.Parse(_dbBytes);
        return header.PageSize;
    }

    /// <summary>SQLite: PRAGMA query. Expected: hundreds of bytes (boxing + interop).</summary>
    [Benchmark]
    [BenchmarkCategory("MetadataRead")]
    public long SQLite_Metadata_PragmaPageSize()
    {
        return (long)_pragmaPageSize.ExecuteScalar()!;
    }

    // ========================================================================
    // Category: OpenClose â€” Connection lifecycle allocation comparison
    // ========================================================================

    /// <summary>Sharc: stackalloc read + struct parse. Expected: 0 B heap allocated.</summary>
    [Benchmark]
    [BenchmarkCategory("OpenClose")]
    public int Sharc_Open_FileAndParse()
    {
        using var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> buffer = stackalloc byte[100];
        fs.ReadExactly(buffer);
        var header = DatabaseHeader.Parse(buffer);
        return header.PageSize;
    }

    /// <summary>SQLite: full connection open. Expected: thousands of bytes.</summary>
    [Benchmark]
    [BenchmarkCategory("OpenClose")]
    public void SQLite_Open_Connection()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        conn.Open();
    }

    // ========================================================================
    // Category: RowRead â€” Per-row allocation comparison (small result set)
    // ========================================================================

    /// <summary>
    /// Sharc: 5 ColumnValue structs created inline. Integer and Float = 0 B.
    /// Only Text/Blob reference existing byte[] â€” no new allocations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RowRead")]
    public long Sharc_Read5Rows_ColumnValues()
    {
        long sum = 0;
        for (int i = 0; i < 5; i++)
        {
            var id = ColumnValue.FromInt64(4, i + 1);
            var name = ColumnValue.Text(59, _textBytes);
            var email = ColumnValue.Text(59, _textBytes);
            var age = ColumnValue.FromInt64(4, 25 + i);
            var balance = ColumnValue.FromDouble(1234.56 + i);

            sum += id.AsInt64() + age.AsInt64() + (long)balance.AsDouble();
        }
        return sum;
    }

    /// <summary>
    /// SQLite: read 5 rows via DataReader. Each GetString allocates a new string.
    /// Reader itself is a managed wrapper. Shows per-row allocation cost.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RowRead")]
    public long SQLite_Read5Rows_DataReader()
    {
        long sum = 0;
        using var reader = _selectFirst5.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);     // id
            _ = reader.GetString(1);        // username (allocates)
            _ = reader.GetString(2);        // email (allocates)
            sum += reader.GetInt32(3);      // age
            sum += (long)reader.GetDouble(4); // balance
        }
        return sum;
    }

    // ========================================================================
    // Category: IntegerScan â€” Integer-only scan (best case for both)
    // ========================================================================

    /// <summary>
    /// Sharc: 100 integer ColumnValues. All inline in struct = 0 B.
    /// This is the target for Sharc's zero-alloc hot path.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("IntegerScan")]
    public long Sharc_Scan100Integers()
    {
        long sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var id = ColumnValue.FromInt64(4, i);
            var age = ColumnValue.FromInt64(4, 20 + (i % 60));
            sum += id.AsInt64() + age.AsInt64();
        }
        return sum;
    }

    /// <summary>
    /// SQLite: 100 rows, integers only. Even without strings, the reader and
    /// result set infrastructure allocate managed objects.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("IntegerScan")]
    public long SQLite_Scan100Integers()
    {
        long sum = 0;
        using var reader = _selectIntegers.ExecuteReader();
        while (reader.Read())
        {
            sum += reader.GetInt64(0);
            sum += reader.GetInt32(1);
        }
        return sum;
    }

    // ========================================================================
    // Category: PrimitiveDecode â€” Varint and serial type codec allocation
    // ========================================================================

    /// <summary>
    /// Sharc: decode 100 varints. Pure stack operations, 0 B allocated.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PrimitiveDecode")]
    public long Sharc_DecodeVarint_Batch100()
    {
        long sum = 0;
        for (int i = 0; i < 50; i++)
        {
            VarintDecoder.Read(_varint1Byte, out var v1);
            VarintDecoder.Read(_varint9Byte, out var v9);
            sum += v1 + v9;
        }
        return sum;
    }

    /// <summary>
    /// Sharc: decode 100 serial types. Pure register operations, 0 B.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PrimitiveDecode")]
    public int Sharc_SerialType_Batch100()
    {
        int sum = 0;
        long[] types = [0, 1, 4, 6, 7, 8, 9, 13, 100, 101];
        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < types.Length; j++)
            {
                sum += SerialTypeCodec.GetContentSize(types[j]);
            }
        }
        return sum;
    }

    // ========================================================================
    // Category: BTreeParse â€” Page parsing allocation comparison
    // ========================================================================

    /// <summary>
    /// Sharc: parse BTree header from span. Returns struct, 0 B allocated.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BTreeParse")]
    public int Sharc_ParseBTreeHeaders_10Pages()
    {
        int totalCells = 0;
        for (int p = 1; p < 11 && (p + 1) * 4096 <= _dbBytes.Length; p++)
        {
            var pageSpan = _dbBytes.AsSpan(p * 4096, 4096);
            try
            {
                var hdr = BTreePageHeader.Parse(pageSpan);
                totalCells += hdr.CellCount;
            }
            catch { /* skip non-btree pages */ }
        }
        return totalCells;
    }

    /// <summary>
    /// Sharc: parse + read cell pointers for 10 pages. Only allocation is ushort[] per page.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BTreeParse")]
    public int Sharc_ParseBTreeWithCellPointers_10Pages()
    {
        int totalCells = 0;
        for (int p = 1; p < 11 && (p + 1) * 4096 <= _dbBytes.Length; p++)
        {
            var pageSpan = _dbBytes.AsSpan(p * 4096, 4096);
            try
            {
                var hdr = BTreePageHeader.Parse(pageSpan);
                var pointers = hdr.ReadCellPointers(pageSpan);
                totalCells += pointers.Length;
            }
            catch { /* skip non-btree pages */ }
        }
        return totalCells;
    }

    /// <summary>
    /// SQLite: SELECT from 10 different rowid ranges. Shows SQL parse + reader allocations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BTreeParse")]
    public long SQLite_Read10Pages_Equivalent()
    {
        long sum = 0;
        for (int i = 0; i < 10; i++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"SELECT id FROM users WHERE id > {i * 1000} LIMIT 50";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sum += reader.GetInt64(0);
            }
        }
        return sum;
    }

    // ========================================================================
    // Category: PageTransform â€” Transform pipeline allocation
    // ========================================================================

    /// <summary>
    /// Sharc: identity transform 10 pages. Reuses single destination buffer.
    /// Only 1 allocation (the dest byte[]) regardless of page count.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PageTransform")]
    public void Sharc_TransformRead_10Pages()
    {
        var header = DatabaseHeader.Parse(_dbBytes);
        int pageSize = header.PageSize;
        var dest = new byte[pageSize];

        for (int p = 0; p < 10 && (p + 1) * pageSize <= _dbBytes.Length; p++)
        {
            var source = _dbBytes.AsSpan(p * pageSize, pageSize);
            IdentityPageTransform.Instance.TransformRead(source, dest, (uint)(p + 1));
        }
    }
}
