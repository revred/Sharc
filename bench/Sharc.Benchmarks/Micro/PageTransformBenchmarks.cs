using BenchmarkDotNet.Attributes;
using Sharc.Core;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for page transform throughput.
/// IdentityPageTransform (no-op copy) establishes the baseline for future
/// encrypted transforms (AES-256-GCM, XChaCha20-Poly1305).
///
/// Memory profile:
///   All transforms operate on pre-allocated buffers: 0 B per operation.
///   The transform interface uses Span&lt;T&gt; — no boxing, no allocation.
///   Batch tests confirm 0 B total regardless of iteration count.
/// </summary>
[BenchmarkCategory("Micro", "IO", "PageTransform")]
[MemoryDiagnoser]
public class PageTransformBenchmarks
{
    private byte[] _source = null!;
    private byte[] _destination = null!;

    [Params(1024, 4096, 8192, 16384, 65536)]
    public int PageSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = new byte[PageSize];
        _destination = new byte[PageSize];
        new Random(42).NextBytes(_source);
    }

    [Benchmark(Baseline = true)]
    public void IdentityTransformRead()
    {
        IdentityPageTransform.Instance.TransformRead(_source, _destination, 1);
    }

    [Benchmark]
    public void IdentityTransformWrite()
    {
        IdentityPageTransform.Instance.TransformWrite(_source, _destination, 1);
    }

    [Benchmark]
    public int TransformedPageSize()
    {
        return IdentityPageTransform.Instance.TransformedPageSize(PageSize);
    }

    /// <summary>
    /// 100 page transforms with reused buffers. 0 B allocated.
    /// Shows that page processing pipeline has zero GC pressure.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public void TransformRead_100Pages()
    {
        for (uint i = 0; i < 100; i++)
        {
            IdentityPageTransform.Instance.TransformRead(_source, _destination, i + 1);
        }
    }

    /// <summary>
    /// 1000 page transforms. Still 0 B. Validates no hidden allocations at scale.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public void TransformRead_1000Pages()
    {
        for (uint i = 0; i < 1000; i++)
        {
            IdentityPageTransform.Instance.TransformRead(_source, _destination, i + 1);
        }
    }

    /// <summary>
    /// Alternating read/write transforms. 0 B — interface dispatch doesn't allocate.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public void TransformReadWrite_50Each()
    {
        for (uint i = 0; i < 50; i++)
        {
            IdentityPageTransform.Instance.TransformRead(_source, _destination, i + 1);
            IdentityPageTransform.Instance.TransformWrite(_destination, _source, i + 1);
        }
    }
}
