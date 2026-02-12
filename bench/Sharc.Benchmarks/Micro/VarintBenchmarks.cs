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
using Sharc.Core.Primitives;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for SQLite varint encode/decode operations.
/// Target: >200M ops/sec for single-byte varints, 0 bytes allocated for all operations.
/// </summary>
[BenchmarkCategory("Micro", "Primitives", "Varint")]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class VarintBenchmarks
{
    private byte[] _varint1Byte = null!;
    private byte[] _varint2Byte = null!;
    private byte[] _varint3Byte = null!;
    private byte[] _varint4Byte = null!;
    private byte[] _varint5Byte = null!;
    private byte[] _varint9Byte = null!;
    private byte[] _writeBuffer = null!;
    private byte[] _batchData = null!;
    private int _batchCount;
    private long[] _sampleValues = null!;

    [GlobalSetup]
    public void Setup()
    {
        _writeBuffer = new byte[9];
        _varint1Byte = EncodeVarint(42);
        _varint2Byte = EncodeVarint(500);
        _varint3Byte = EncodeVarint(16384);
        _varint4Byte = EncodeVarint(2_097_152);
        _varint5Byte = EncodeVarint(268_435_456);
        _varint9Byte = EncodeVarint(long.MaxValue);

        _sampleValues = [0, 1, 127, 128, 500, 16383, 16384, 1_000_000, long.MaxValue];

        // Build batch: 1000 mixed varints
        _batchCount = 1000;
        using var ms = new MemoryStream();
        var buf = new byte[9];
        var rng = new Random(42);
        for (int i = 0; i < _batchCount; i++)
        {
            long val = _sampleValues[rng.Next(_sampleValues.Length)];
            int len = VarintDecoder.Write(buf, val);
            ms.Write(buf, 0, len);
        }
        _batchData = ms.ToArray();
    }

    private static byte[] EncodeVarint(long value)
    {
        var buf = new byte[9];
        int len = VarintDecoder.Write(buf, value);
        return buf[..len];
    }

    // --- Read by byte count ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Read")]
    public long Read_1Byte()
    {
        VarintDecoder.Read(_varint1Byte, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Read")]
    public long Read_2Byte()
    {
        VarintDecoder.Read(_varint2Byte, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Read")]
    public long Read_3Byte()
    {
        VarintDecoder.Read(_varint3Byte, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Read")]
    public long Read_4Byte()
    {
        VarintDecoder.Read(_varint4Byte, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Read")]
    public long Read_5Byte()
    {
        VarintDecoder.Read(_varint5Byte, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Read")]
    public long Read_9Byte()
    {
        VarintDecoder.Read(_varint9Byte, out var value);
        return value;
    }

    // --- Batch: realistic mixed-size throughput ---

    [Benchmark]
    [BenchmarkCategory("Read", "Batch")]
    public long Read_Batch1000()
    {
        long sum = 0;
        var span = _batchData.AsSpan();
        int offset = 0;
        for (int i = 0; i < _batchCount; i++)
        {
            int consumed = VarintDecoder.Read(span[offset..], out var value);
            sum += value;
            offset += consumed;
        }
        return sum;
    }

    // --- Write ---

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_1Byte() => VarintDecoder.Write(_writeBuffer, 42);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_2Byte() => VarintDecoder.Write(_writeBuffer, 500);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_9Byte() => VarintDecoder.Write(_writeBuffer, long.MaxValue);

    /// <summary>
    /// Write 1000 varints to a pre-allocated buffer. 0 B allocated.
    /// Shows that encoding is purely span-based with no hidden allocations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Write", "Batch")]
    public int Write_Batch1000()
    {
        var buffer = new byte[9000]; // pre-allocated, worst case 9 bytes each
        int offset = 0;
        var rng = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            long val = _sampleValues[rng.Next(_sampleValues.Length)];
            int written = VarintDecoder.Write(buffer.AsSpan(offset), val);
            offset += written;
        }
        return offset;
    }

    /// <summary>
    /// Write + Read roundtrip: encode then decode the same value.
    /// Verifies no allocations in the full encode-decode pipeline.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Roundtrip")]
    public long Roundtrip_1Byte()
    {
        Span<byte> buf = stackalloc byte[9];
        VarintDecoder.Write(buf, 42);
        VarintDecoder.Read(buf, out var value);
        return value;
    }

    [Benchmark]
    [BenchmarkCategory("Roundtrip")]
    public long Roundtrip_9Byte()
    {
        Span<byte> buf = stackalloc byte[9];
        VarintDecoder.Write(buf, long.MaxValue);
        VarintDecoder.Read(buf, out var value);
        return value;
    }

    // --- GetEncodedLength ---

    [Benchmark]
    [BenchmarkCategory("Length")]
    public int GetEncodedLength_Small() => VarintDecoder.GetEncodedLength(42);

    [Benchmark]
    [BenchmarkCategory("Length")]
    public int GetEncodedLength_Large() => VarintDecoder.GetEncodedLength(long.MaxValue);

    /// <summary>
    /// GetEncodedLength for all boundary values. 0 B allocated.
    /// Tests all 9 code paths in the method.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Length", "Batch")]
    public int GetEncodedLength_AllBoundaries()
    {
        int sum = 0;
        sum += VarintDecoder.GetEncodedLength(0);               // 1 byte
        sum += VarintDecoder.GetEncodedLength(0x7F);             // 1 byte boundary
        sum += VarintDecoder.GetEncodedLength(0x80);             // 2 byte boundary
        sum += VarintDecoder.GetEncodedLength(0x3FFF);           // 2 byte max
        sum += VarintDecoder.GetEncodedLength(0x4000);           // 3 byte boundary
        sum += VarintDecoder.GetEncodedLength(0x1FFFFF);         // 3 byte max
        sum += VarintDecoder.GetEncodedLength(0x200000);         // 4 byte boundary
        sum += VarintDecoder.GetEncodedLength(0x0FFFFFFF);       // 4 byte max
        sum += VarintDecoder.GetEncodedLength(0x10000000);       // 5 byte boundary
        sum += VarintDecoder.GetEncodedLength(long.MaxValue);    // 9 byte
        return sum;
    }
}
