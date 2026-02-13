/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Comparisons;

/// <summary>
/// Defines benchmark tiers for the graph + core comparison suite.
///
/// Usage:
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --tier micro
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --tier mini
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --tier standard
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --tier mega
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --tier full
///
/// Tiers:
///   micro    ~4 benchmarks,  ~1 min   Engine load + one scan pair
///   mini     ~10 benchmarks, ~3 min   Core pairs + graph seek
///   standard ~26 benchmarks, ~8 min   Core + graph scan + graph seek
///   mega     all benchmarks, ~12 min  Everything incl. GC pressure + traversal
///   full     (same as mega for this project)
/// </summary>
internal static class BenchmarkTiers
{
    internal static string[]? GetFilters(string tier) => tier.ToLowerInvariant() switch
    {
        "micro" => Micro,
        "mini" => Mini,
        "standard" => Standard,
        "mega" => null,
        "full" => null,
        _ => null,
    };

    internal static bool IsKnown(string tier) => tier.ToLowerInvariant() is
        "micro" or "mini" or "standard" or "mega" or "full";

    // ── Micro: ~4 benchmarks, ~1 min ──
    private static readonly string[] Micro =
    [
        "*CoreBenchmarks.*EngineLoad*",
        "*CoreBenchmarks.*SequentialScan*",
    ];

    // ── Mini: ~10 benchmarks, ~3 min ──
    private static readonly string[] Mini =
    [
        "*CoreBenchmarks.*EngineLoad*",
        "*CoreBenchmarks.*SchemaRead*",
        "*CoreBenchmarks.*SequentialScan*",
        "*CoreBenchmarks.*PointLookup*",
        "*GraphSeekBenchmarks.*SingleSeek*",
    ];

    // ── Standard: ~26 benchmarks, ~8 min ──
    // All core + graph scan + graph seek (skip GC pressure + traversal)
    private static readonly string[] Standard =
    [
        "*CoreBenchmarks.*EngineLoad*",
        "*CoreBenchmarks.*SchemaRead*",
        "*CoreBenchmarks.*SequentialScan*",
        "*CoreBenchmarks.*PointLookup*",
        "*CoreBenchmarks.*BatchLookup*",
        "*CoreBenchmarks.*TypeDecode*",
        "*CoreBenchmarks.*NullScan*",
        "*CoreBenchmarks.*WhereFilter*",
        "*GraphScanBenchmarks*",
        "*GraphSeekBenchmarks*",
    ];

    // Mega/Full: null (no filter) — runs everything
}
