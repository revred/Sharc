using BenchmarkDotNet.Attributes;

namespace Sharc.Benchmarks.Future;

/// <summary>
/// PLACEHOLDER: Record decode benchmarks.
/// Requires: IRecordDecoder implementation (Milestone 3).
/// Planned: DecodeRecord with integer-only, mixed types, single column projection,
/// column count without full decode, and batch of 1000 records.
/// </summary>
[BenchmarkCategory("Future", "RecordDecode")]
[MemoryDiagnoser]
public class RecordDecodeBenchmarks
{
    // Activate when IRecordDecoder implementation exists.
}
