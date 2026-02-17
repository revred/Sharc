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
using Sharc.Core;
using Sharc.Core.Primitives;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for GUID storage paths:
/// BLOB(16) — single 16-byte big-endian blob (serial type 44).
/// Merged Int64 pair — two 8-byte integers (serial type 6 × 2), zero-alloc.
/// </summary>
[BenchmarkCategory("Micro", "GUID")]
[MemoryDiagnoser]
public class GuidCodecBenchmarks
{
    private Guid _guid;
    private byte[] _blobBytes = null!;
    private long _hi, _lo;
    private ColumnValue _blobValue;
    private ColumnValue _hiValue, _loValue;

    [GlobalSetup]
    public void Setup()
    {
        _guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        _blobBytes = new byte[16];
        GuidCodec.Encode(_guid, _blobBytes);
        (_hi, _lo) = GuidCodec.ToInt64Pair(_guid);
        _blobValue = ColumnValue.FromGuid(_guid);
        _hiValue = ColumnValue.FromInt64(6, _hi);
        _loValue = ColumnValue.FromInt64(6, _lo);
    }

    // --- Encode: Guid → storage format ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode")]
    public ColumnValue Encode_Blob16()
    {
        return ColumnValue.FromGuid(_guid);
    }

    [Benchmark]
    [BenchmarkCategory("Encode")]
    public (ColumnValue, ColumnValue) Encode_MergedInt64Pair()
    {
        return ColumnValue.SplitGuidForMerge(_guid);
    }

    // --- Decode: storage format → Guid ---

    [Benchmark]
    [BenchmarkCategory("Decode")]
    public Guid Decode_Blob16()
    {
        return GuidCodec.Decode(_blobBytes);
    }

    [Benchmark]
    [BenchmarkCategory("Decode")]
    public Guid Decode_MergedInt64Pair()
    {
        return GuidCodec.FromInt64Pair(_hi, _lo);
    }

    // --- Round-trip: Guid → encode → decode → Guid ---

    [Benchmark]
    [BenchmarkCategory("RoundTrip")]
    public Guid RoundTrip_Blob16()
    {
        var bytes = new byte[16];
        GuidCodec.Encode(_guid, bytes);
        return GuidCodec.Decode(bytes);
    }

    [Benchmark]
    [BenchmarkCategory("RoundTrip")]
    public Guid RoundTrip_MergedInt64Pair()
    {
        var (hi, lo) = GuidCodec.ToInt64Pair(_guid);
        return GuidCodec.FromInt64Pair(hi, lo);
    }

    // --- Batch: 1000 GUIDs ---

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public int Batch_Blob16_1000()
    {
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            var cv = ColumnValue.FromGuid(_guid);
            if (!cv.IsNull) count++;
        }
        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public int Batch_MergedInt64Pair_1000()
    {
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            var (hi, lo) = ColumnValue.SplitGuidForMerge(_guid);
            if (!hi.IsNull) count++;
        }
        return count;
    }
}
