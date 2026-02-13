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

namespace Sharc.Benchmarks;

/// <summary>
/// Defines benchmark tiers for fast, incremental feedback.
/// Each tier is cumulative — higher tiers include all lower-tier benchmarks.
///
/// Usage:
///   dotnet run -c Release --project bench/Sharc.Benchmarks -- --tier micro
///   dotnet run -c Release --project bench/Sharc.Benchmarks -- --tier mini
///   dotnet run -c Release --project bench/Sharc.Benchmarks -- --tier standard
///   dotnet run -c Release --project bench/Sharc.Benchmarks -- --tier mega
///   dotnet run -c Release --project bench/Sharc.Benchmarks -- --tier full
///
/// Tiers:
///   micro    ~6 benchmarks,   ~1.5 min  Key primitives + one Sharc vs SQLite pair
///   mini     ~15 benchmarks,  ~4 min    Representative cross-section for PR feedback
///   standard ~80 benchmarks,  ~20 min   All Sharc vs SQLite comparative pairs
///   mega     ~200 benchmarks, ~50 min   All micro + all comparative classes
///   full     (same as mega — expands when Future benchmarks are added)
/// </summary>
internal static class BenchmarkTiers
{
    /// <summary>
    /// Returns filter patterns for the given tier name, or null for "full" / unknown.
    /// </summary>
    internal static string[]? GetFilters(string tier) => tier.ToLowerInvariant() switch
    {
        "micro" => Micro,
        "mini" => Mini,
        "standard" => Standard,
        "mega" => Mega,
        "full" => null,
        _ => null,
    };

    /// <summary>
    /// Returns true if the given string is a recognized tier name.
    /// </summary>
    internal static bool IsKnown(string tier) => tier.ToLowerInvariant() is
        "micro" or "mini" or "standard" or "mega" or "full";

    // ── Micro: ~6 benchmarks, ~1 min ──
    // Quick sanity: one key primitive from each area + one Sharc vs SQLite pair
    private static readonly string[] Micro =
    [
        "*VarintBenchmarks.Read_1Byte",
        "*VarintBenchmarks.Read_9Byte",
        "*SerialTypeCodecBenchmarks.GetContentSize_Null",
        "*DatabaseHeaderBenchmarks.Parse_4096PageSize",
        "*TableScanBenchmarks.*_Scan100_*",
    ];

    // ── Mini: ~15 benchmarks, ~4 min ──
    // Representative cross-section — one from each key area
    private static readonly string[] Mini =
    [
        // Primitives — fast reads only
        "*VarintBenchmarks.Read_1Byte",
        "*VarintBenchmarks.Read_9Byte",
        "*VarintBenchmarks.Read_Batch1000",
        "*SerialTypeCodecBenchmarks.GetContentSize_Null",
        "*SerialTypeCodecBenchmarks.GetContentSize_Int64",
        "*SerialTypeCodecBenchmarks.GetContentSize_Text44",
        "*DatabaseHeaderBenchmarks.Parse_4096PageSize",
        // Records — one create, one access
        "*ColumnValueBenchmarks.Create_Integer",
        "*ColumnValueBenchmarks.Access_Int64",
        // Comparative — one pair per key category
        "*DatabaseOpenBenchmarks.*Memory_Small*",
        "*TableScanBenchmarks.*_Scan100_*",
        "*TypeDecodeBenchmarks.*_Integers*",
        "*RealisticWorkloadBenchmarks.*OpenReadClose*",
    ];

    // ── Standard: ~80 benchmarks, ~20 min ──
    // All Sharc vs SQLite comparative benchmarks (skip GC/memory stress, page read)
    private static readonly string[] Standard =
    [
        "*TableScanBenchmarks*",
        "*DatabaseOpenBenchmarks*",
        "*TypeDecodeBenchmarks*",
        "*SchemaMetadataBenchmarks*",
        "*HeaderRetrievalBenchmarks*",
        "*RealisticWorkloadBenchmarks*",
    ];

    // ── Mega: ~200 benchmarks, ~50 min ──
    // Everything: all Micro + all Comparative (incl. GC, memory, page read, page transform)
    private static readonly string[] Mega =
    [
        "*Micro.*",
        "*Comparative.*",
    ];

    // Full: null (no filter) — runs everything including Future + parameterized
}
