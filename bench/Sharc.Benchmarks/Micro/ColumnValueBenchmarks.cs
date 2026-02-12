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
using Sharc.Core;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for ColumnValue struct creation and access.
/// Created once per column per row during record decoding.
/// Target: 0 bytes allocated for Integer, Float, and Null columns.
/// Text/Blob reference existing byte[] memory â€” no per-column allocation.
/// AsString() allocates a new string (expected).
/// </summary>
[BenchmarkCategory("Micro", "Records", "ColumnValue")]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 2)]
public class ColumnValueBenchmarks
{
    private byte[] _textBytes = null!;
    private byte[] _blobBytes = null!;
    private ColumnValue _intValue;
    private ColumnValue _floatValue;
    private ColumnValue _textValue;
    private ColumnValue _blobValue;
    private ColumnValue _nullValue;

    [GlobalSetup]
    public void Setup()
    {
        _textBytes = "Hello, Sharc benchmark!"u8.ToArray();
        _blobBytes = new byte[64];
        new Random(42).NextBytes(_blobBytes);

        _intValue = ColumnValue.FromInt64(4, 42);
        _floatValue = ColumnValue.FromDouble(3.14159);
        _textValue = ColumnValue.Text(59, _textBytes);
        _blobValue = ColumnValue.Blob(140, _blobBytes);
        _nullValue = ColumnValue.Null();
    }

    // --- Factory methods (all 0 B allocated for structs) ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Create")]
    public ColumnValue Create_Null() => ColumnValue.Null();

    [Benchmark]
    [BenchmarkCategory("Create")]
    public ColumnValue Create_Integer() => ColumnValue.FromInt64(4, 42);

    [Benchmark]
    [BenchmarkCategory("Create")]
    public ColumnValue Create_Float() => ColumnValue.FromDouble(3.14159);

    [Benchmark]
    [BenchmarkCategory("Create")]
    public ColumnValue Create_Text() => ColumnValue.Text(59, _textBytes);

    [Benchmark]
    [BenchmarkCategory("Create")]
    public ColumnValue Create_Blob() => ColumnValue.Blob(140, _blobBytes);

    // --- Accessors (0 B for int/float/bytes/isNull, allocates for AsString) ---

    [Benchmark]
    [BenchmarkCategory("Access")]
    public long Access_Int64() => _intValue.AsInt64();

    [Benchmark]
    [BenchmarkCategory("Access")]
    public double Access_Double() => _floatValue.AsDouble();

    /// <summary>
    /// AsString() allocates a new string â€” this is expected and unavoidable.
    /// This benchmark shows the cost of string materialization.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Access")]
    public string Access_String() => _textValue.AsString();

    /// <summary>
    /// AsBytes() returns ReadOnlyMemory wrapping existing array â€” 0 B allocated.
    /// This is the preferred path for zero-alloc text access.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Access")]
    public ReadOnlyMemory<byte> Access_Bytes() => _textValue.AsBytes();

    [Benchmark]
    [BenchmarkCategory("Access")]
    public bool Access_IsNull() => _nullValue.IsNull;

    [Benchmark]
    [BenchmarkCategory("Access")]
    public ReadOnlyMemory<byte> Access_BlobBytes() => _blobValue.AsBytes();

    [Benchmark]
    [BenchmarkCategory("Access")]
    public long Access_StorageClass() => (long)_intValue.StorageClass;

    [Benchmark]
    [BenchmarkCategory("Access")]
    public long Access_SerialType() => _intValue.SerialType;

    // --- Simulated row decode ---

    /// <summary>
    /// Decode a typical 6-column row using only struct operations.
    /// Integer/Float/Null columns: 0 B. Text/Blob: reference existing bytes, 0 B.
    /// Only AsInt64()/AsDouble() accessed â€” no string materialization.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public long DecodeRow_6Columns()
    {
        var c0 = ColumnValue.FromInt64(4, 1);
        var c1 = ColumnValue.Text(59, _textBytes);
        var c2 = ColumnValue.FromInt64(4, 25);
        var c3 = ColumnValue.FromDouble(99.95);
        var c4 = ColumnValue.Blob(140, _blobBytes);
        var c5 = ColumnValue.Null();
        return c0.AsInt64() + c2.AsInt64() + (c5.IsNull ? 0 : 1);
    }

    /// <summary>
    /// Decode 100 rows of 6 columns each, accessing integers only (no strings).
    /// Expected: 0 B allocated â€” proves per-row zero-alloc for integer-only access.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public long DecodeRows_100x6_IntegersOnly()
    {
        long sum = 0;
        for (int row = 0; row < 100; row++)
        {
            var c0 = ColumnValue.FromInt64(4, row);
            var c1 = ColumnValue.Text(59, _textBytes);
            var c2 = ColumnValue.FromInt64(4, 25 + row);
            var c3 = ColumnValue.FromDouble(99.95 + row);
            var c4 = ColumnValue.Blob(140, _blobBytes);
            var c5 = ColumnValue.Null();
            sum += c0.AsInt64() + c2.AsInt64();
        }
        return sum;
    }

    /// <summary>
    /// Decode 100 rows and materialize string for each text column.
    /// Shows the per-row cost of string allocation (the only alloc in the pipeline).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public int DecodeRows_100x6_WithStringAccess()
    {
        int totalLen = 0;
        for (int row = 0; row < 100; row++)
        {
            var c0 = ColumnValue.FromInt64(4, row);
            var c1 = ColumnValue.Text(59, _textBytes);
            var c2 = ColumnValue.FromInt64(4, 25 + row);
            var c3 = ColumnValue.FromDouble(99.95 + row);
            var c4 = ColumnValue.Blob(140, _blobBytes);
            var c5 = ColumnValue.Null();

            totalLen += c1.AsString().Length; // only allocation point
        }
        return totalLen;
    }

    /// <summary>
    /// Decode 100 rows and access text as raw bytes (zero-alloc path).
    /// Proves that avoiding AsString() gives 0 B total allocation.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public int DecodeRows_100x6_WithBytesAccess()
    {
        int totalLen = 0;
        for (int row = 0; row < 100; row++)
        {
            var c0 = ColumnValue.FromInt64(4, row);
            var c1 = ColumnValue.Text(59, _textBytes);
            var c2 = ColumnValue.FromInt64(4, 25 + row);
            var c3 = ColumnValue.FromDouble(99.95 + row);
            var c4 = ColumnValue.Blob(140, _blobBytes);
            var c5 = ColumnValue.Null();

            totalLen += c1.AsBytes().Length; // 0 B â€” wraps existing array
        }
        return totalLen;
    }
}
