using BenchmarkDotNet.Attributes;
using Sharc.Core;
using Sharc.Core.Primitives;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for serial type code interpretation.
/// Called once per column per row during record decoding — extremely hot path.
/// All operations are pure computation on scalar values: 0 B allocated.
/// </summary>
[BenchmarkCategory("Micro", "Primitives", "SerialType")]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class SerialTypeCodecBenchmarks
{
    private long[] _serialTypes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _serialTypes =
        [
            0,   // NULL
            1,   // 8-bit int
            2,   // 16-bit int
            3,   // 24-bit int
            4,   // 32-bit int
            5,   // 48-bit int
            6,   // 64-bit int
            7,   // float
            8,   // constant 0
            9,   // constant 1
            13,  // empty text
            15,  // 1-char text
            101, // 44-char text
            12,  // empty blob
            14,  // 1-byte blob
            100, // 44-byte blob
        ];
    }

    // --- GetContentSize: all integer sizes + special types ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Null() => SerialTypeCodec.GetContentSize(0);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int8() => SerialTypeCodec.GetContentSize(1);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int16() => SerialTypeCodec.GetContentSize(2);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int24() => SerialTypeCodec.GetContentSize(3);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int32() => SerialTypeCodec.GetContentSize(4);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int48() => SerialTypeCodec.GetContentSize(5);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Int64() => SerialTypeCodec.GetContentSize(6);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Float() => SerialTypeCodec.GetContentSize(7);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_ConstantZero() => SerialTypeCodec.GetContentSize(8);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_ConstantOne() => SerialTypeCodec.GetContentSize(9);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Text44() => SerialTypeCodec.GetContentSize(101);

    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_Blob44() => SerialTypeCodec.GetContentSize(100);

    /// <summary>
    /// Large serial type (TEXT with 500 chars). Tests the formula path: (serialType - 12) / 2.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ContentSize")]
    public int GetContentSize_LargeText() => SerialTypeCodec.GetContentSize(1013);

    /// <summary>
    /// All serial types in sequence. 0 B allocated. Shows branch prediction patterns.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ContentSize", "Batch")]
    public int GetContentSize_BatchAll()
    {
        int sum = 0;
        for (int i = 0; i < _serialTypes.Length; i++)
            sum += SerialTypeCodec.GetContentSize(_serialTypes[i]);
        return sum;
    }

    /// <summary>
    /// 1000 iterations over all types. Shows throughput at scale, confirms 0 B total.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ContentSize", "Batch")]
    public int GetContentSize_Batch1000()
    {
        int sum = 0;
        for (int iter = 0; iter < 1000; iter++)
        {
            for (int i = 0; i < _serialTypes.Length; i++)
                sum += SerialTypeCodec.GetContentSize(_serialTypes[i]);
        }
        return sum;
    }

    // --- GetStorageClass ---

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_Null() => SerialTypeCodec.GetStorageClass(0);

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_Integer() => SerialTypeCodec.GetStorageClass(4);

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_Float() => SerialTypeCodec.GetStorageClass(7);

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_ConstantZero() => SerialTypeCodec.GetStorageClass(8);

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_Text() => SerialTypeCodec.GetStorageClass(101);

    [Benchmark]
    [BenchmarkCategory("StorageClass")]
    public ColumnStorageClass GetStorageClass_Blob() => SerialTypeCodec.GetStorageClass(100);

    /// <summary>
    /// Batch GetStorageClass for all types. 0 B — enum return, no boxing.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("StorageClass", "Batch")]
    public int GetStorageClass_BatchAll()
    {
        int sum = 0;
        for (int i = 0; i < _serialTypes.Length; i++)
            sum += (int)SerialTypeCodec.GetStorageClass(_serialTypes[i]);
        return sum;
    }

    // --- Predicate methods ---

    [Benchmark]
    [BenchmarkCategory("Predicate")]
    public bool IsNull() => SerialTypeCodec.IsNull(0);

    [Benchmark]
    [BenchmarkCategory("Predicate")]
    public bool IsInteger() => SerialTypeCodec.IsInteger(4);

    [Benchmark]
    [BenchmarkCategory("Predicate")]
    public bool IsFloat() => SerialTypeCodec.IsFloat(7);

    [Benchmark]
    [BenchmarkCategory("Predicate")]
    public bool IsText() => SerialTypeCodec.IsText(101);

    [Benchmark]
    [BenchmarkCategory("Predicate")]
    public bool IsBlob() => SerialTypeCodec.IsBlob(100);

    /// <summary>
    /// All 5 predicates on all 16 types = 80 checks. 0 B allocated.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Predicate", "Batch")]
    public int IsPredicates_BatchAll()
    {
        int count = 0;
        for (int i = 0; i < _serialTypes.Length; i++)
        {
            long st = _serialTypes[i];
            if (SerialTypeCodec.IsNull(st)) count++;
            if (SerialTypeCodec.IsInteger(st)) count++;
            if (SerialTypeCodec.IsFloat(st)) count++;
            if (SerialTypeCodec.IsText(st)) count++;
            if (SerialTypeCodec.IsBlob(st)) count++;
        }
        return count;
    }

    // --- Combined: typical record decode pattern ---

    /// <summary>
    /// Simulates decoding the serial type header of a 6-column record:
    /// GetContentSize + GetStorageClass for each column. 0 B allocated.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int DecodeRecordHeader_6Columns()
    {
        long[] recordTypes = [4, 6, 101, 7, 100, 0]; // int32, int64, text, float, blob, null
        int totalSize = 0;
        for (int i = 0; i < recordTypes.Length; i++)
        {
            totalSize += SerialTypeCodec.GetContentSize(recordTypes[i]);
            _ = SerialTypeCodec.GetStorageClass(recordTypes[i]);
        }
        return totalSize;
    }
}
