using BenchmarkDotNet.Attributes;

namespace Sharc.Benchmarks.Future;

/// <summary>
/// PLACEHOLDER: Full table scan benchmarks comparing Sharc vs Microsoft.Data.Sqlite.
/// Requires: SharcDatabase.Open, CreateReader, BTreeReader, RecordDecoder (Milestone 4+).
/// Target: within 3x of Microsoft.Data.Sqlite for full table scan.
/// Allocation target: 0 bytes per row for primitive columns.
/// </summary>
[BenchmarkCategory("Future", "TableScan")]
[MemoryDiagnoser]
public class TableScanBenchmarks
{
    // Activate when SharcDatabase.Open and CreateReader are implemented.
}
