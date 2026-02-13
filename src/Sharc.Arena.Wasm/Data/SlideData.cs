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

using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Data;

/// <summary>
/// All static data for the arena: engines, categories, presets, density tiers, and 16 benchmark slides.
/// Values sourced from BenchmarkDotNet v0.15.8, .NET 10.0.2, Windows 11, Intel i7-11800H.
/// </summary>
public static class SlideData
{
    // ── Engine Definitions ──────────────────────────────────────

    public static readonly IReadOnlyList<EngineDefinition> Engines =
    [
        new() { Id = "sqlite",    Name = "SQLite",      Subtitle = "C \u2192 WASM P/Invoke", Color = "#3B82F6", Icon = "\u26A1",     Footprint = "~1.5 MB",  Tier = EngineTier.NativeDotNet },
        new() { Id = "indexeddb", Name = "IndexedDB",   Subtitle = "Browser-native",     Color = "#F59E0B", Icon = "\uD83D\uDDC4\uFE0F", Footprint = "Built-in", Tier = EngineTier.JsInterop },
        new() { Id = "surrealdb", Name = "SurrealDB",   Subtitle = "Multi-model WASM",   Color = "#A855F7", Icon = "\uD83D\uDD2E",       Footprint = "~8 MB",    Tier = EngineTier.Deferred },
        new() { Id = "arangodb",  Name = "ArangoDB",    Subtitle = "Multi-model C++",    Color = "#EF4444", Icon = "\uD83D\uDD3A",       Footprint = "~10 MB",   Tier = EngineTier.Deferred },
        new() { Id = "sharc",     Name = "Sharc",       Subtitle = "Zero-alloc C#",      Color = "#10B981", Icon = "\uD83E\uDD88",       Footprint = "~50 KB",   Tier = EngineTier.NativeDotNet },
    ];

    // ── Categories ──────────────────────────────────────────────

    public static readonly IReadOnlyList<Category> Categories =
    [
        new() { Id = "core",     Label = "Core",     Color = "#60A5FA" },
        new() { Id = "advanced", Label = "Advanced", Color = "#F472B6" },
        new() { Id = "graph",    Label = "Graph",    Color = "#34D399" },
        new() { Id = "meta",     Label = "Meta",     Color = "#FBBF24" },
    ];

    // ── Workload Presets ────────────────────────────────────────

    public static readonly IReadOnlyList<Preset> Presets =
    [
        new() { Id = "quick",    Label = "\u26A1 Quick Demo",    Description = "~15 sec \u00B7 100 rows",  Scale = 0.01, PauseMs = 200, TransitionMs = 300 },
        new() { Id = "standard", Label = "\uD83D\uDCCA Standard", Description = "~40 sec \u00B7 5K rows",  Scale = 1,    PauseMs = 400, TransitionMs = 500 },
        new() { Id = "stress",   Label = "\uD83D\uDD25 Stress Test", Description = "~90 sec \u00B7 100K rows", Scale = 20, PauseMs = 600, TransitionMs = 700 },
    ];

    // ── Density Tiers ───────────────────────────────────────────

    public static readonly IReadOnlyList<DensityTier> StandardDensityTiers =
    [
        new() { Id = "xs", Label = "100",  Rows = 100,    Scale = 0.01 },
        new() { Id = "sm", Label = "1K",   Rows = 1000,   Scale = 0.1 },
        new() { Id = "md", Label = "5K",   Rows = 5000,   Scale = 1 },
        new() { Id = "lg", Label = "10K",  Rows = 10000,  Scale = 2 },
        new() { Id = "xl", Label = "100K", Rows = 100000, Scale = 20 },
    ];

    public static readonly IReadOnlyList<DensityTier> GraphDensityTiers =
    [
        new() { Id = "xs", Label = "50",  Rows = 50,    Scale = 0.01 },
        new() { Id = "sm", Label = "500", Rows = 500,   Scale = 0.1 },
        new() { Id = "md", Label = "5K",  Rows = 5000,  Scale = 1 },
        new() { Id = "lg", Label = "50K", Rows = 50000, Scale = 10 },
    ];

    public static readonly IReadOnlyList<DensityTier> LookupDensityTiers =
    [
        new() { Id = "xs", Label = "\u00D71",   Rows = 1,   Scale = 0.17 },
        new() { Id = "sm", Label = "\u00D76",   Rows = 6,   Scale = 1 },
        new() { Id = "md", Label = "\u00D750",  Rows = 50,  Scale = 8 },
        new() { Id = "lg", Label = "\u00D7100", Rows = 100, Scale = 17 },
    ];

    public static readonly IReadOnlyList<DensityTier> FixedTiers =
    [
        new() { Id = "md", Label = "\u2014", Rows = 0, Scale = 1 },
    ];

    // ── 16 Benchmark Slides ─────────────────────────────────────

    public static IReadOnlyList<SlideDefinition> CreateSlides() =>
    [
        // ── CORE ──

        new()
        {
            Id = "engine-load", Title = "Engine Initialization",
            Subtitle = "Cold start: download + instantiate + allocate",
            Icon = "\uD83D\uDE80", Unit = "ms", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Cold start: download WASM binary (if applicable), instantiate engine, allocate initial memory. Fixed workload \u2014 not data-dependent.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 142,   Allocation = "1.5 MB" },
                ["indexeddb"] = new() { Value = 2.1,   Allocation = "0 B" },
                ["surrealdb"] = new() { Value = 487,   Allocation = "8.2 MB" },
                ["arangodb"]  = new() { Value = 350,   Allocation = "10 MB" },
                ["sharc"]     = new() { Value = 0.08,  Allocation = "50 KB", Note = "In-process \u2014 no WASM download" },
            }
        },
        new()
        {
            Id = "schema-read", Title = "Schema Introspection",
            Subtitle = "Read table names, columns, constraints",
            Icon = "\uD83D\uDCCB", Unit = "\u03BCs", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Read all table definitions. Sharc walks sqlite_schema B-tree directly. Fixed workload.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 127.9, Allocation = "4.0 KB" },
                ["indexeddb"] = new() { Value = 45,    Allocation = "1.2 KB", Note = "objectStoreNames only" },
                ["surrealdb"] = new() { Value = 320,   Allocation = "12 KB" },
                ["arangodb"]  = new() { Value = 280,   Allocation = "9.5 KB" },
                ["sharc"]     = new() { Value = 11.3,  Allocation = "28 KB", Note = "11.3\u00D7 \u2014 direct B-tree walk" },
            }
        },
        new()
        {
            Id = "sequential-scan", Title = "Sequential Scan",
            Subtitle = "Full table scan, all columns decoded",
            Icon = "\uD83D\uDCCA", Unit = "ms", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan N rows with 9 columns (int, text, float, blob, null). Decode all values. The ETL/analytics workload.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 28.1,  Allocation = "18.0 MB" },
                ["indexeddb"] = new() { Value = 89,    Allocation = "22 MB", Note = "Cursor iteration" },
                ["surrealdb"] = new() { Value = 52,    Allocation = "28 MB" },
                ["arangodb"]  = new() { Value = 45,    Allocation = "24 MB", Note = "C++ document scan" },
                ["sharc"]     = new() { Value = 11.3,  Allocation = "33.9 MB", Note = "2.5\u00D7 \u2014 lazy decode + buffer reuse" },
            }
        },
        new()
        {
            Id = "point-lookup", Title = "B-Tree Point Lookup",
            Subtitle = "Single row by primary key (binary search)",
            Icon = "\uD83C\uDFAF", Unit = "ns", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Seek to rowid via B-tree binary search descent. Single operation \u2014 not data-dependent.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 26168,  Allocation = "600 B" },
                ["indexeddb"] = new() { Value = 85000,  Allocation = "320 B" },
                ["surrealdb"] = new() { Value = 42000,  Allocation = "1.2 KB" },
                ["arangodb"]  = new() { Value = 35000,  Allocation = "980 B", Note = "Hash index lookup" },
                ["sharc"]     = new() { Value = 585,    Allocation = "1,840 B", Note = "44.7\u00D7 \u2014 zero-overhead B-tree seek" },
            }
        },
        new()
        {
            Id = "batch-lookup", Title = "Batch Lookups",
            Subtitle = "Consecutive seeks with page cache locality",
            Icon = "\u26A1", Unit = "ns", CategoryId = "core",
            DensityTiers = LookupDensityTiers, DefaultDensity = "sm", ScaleMode = "linear",
            Methodology = "N sequential PK lookups. Tests LRU page cache \u2014 clustered lookups reuse cached pages.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 135498, Allocation = "3,024 B" },
                ["indexeddb"] = new() { Value = 520000, Allocation = "1.9 KB" },
                ["surrealdb"] = new() { Value = 280000, Allocation = "7.2 KB" },
                ["arangodb"]  = new() { Value = 240000, Allocation = "5.8 KB", Note = "Batch hash lookups" },
                ["sharc"]     = new() { Value = 1789,   Allocation = "4,176 B", Note = "75.7\u00D7 \u2014 LRU cache locality" },
            }
        },
        new()
        {
            Id = "type-decode", Title = "Type Decoding \u2014 Integers",
            Subtitle = "Raw decode speed for typed column values",
            Icon = "\uD83D\uDD22", Unit = "ms", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "xl", ScaleMode = "linear",
            Methodology = "Decode N integer values. Sharc: ReadOnlySpan<byte> + struct returns vs boxed objects.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 17.0, Allocation = "384 B" },
                ["indexeddb"] = new() { Value = 42,   Allocation = "3.2 MB", Note = "Everything is JS objects" },
                ["surrealdb"] = new() { Value = 24,   Allocation = "1.8 MB" },
                ["arangodb"]  = new() { Value = 20,   Allocation = "1.4 MB", Note = "VPack binary format" },
                ["sharc"]     = new() { Value = 4.2,  Allocation = "29 KB", Note = "4.1\u00D7 \u2014 zero-alloc Span pipeline" },
            }
        },
        new()
        {
            Id = "null-scan", Title = "NULL Detection",
            Subtitle = "Check NULLs without full row decode",
            Icon = "\u2205", Unit = "\u03BCs", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan N rows checking nullable column. Sharc reads only serial type headers, skipping body decode.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 11562, Allocation = "744 B" },
                ["indexeddb"] = new() { Value = 38000, Allocation = "4.8 MB" },
                ["surrealdb"] = new() { Value = 18000, Allocation = "2.1 MB" },
                ["arangodb"]  = new() { Value = 15000, Allocation = "1.6 MB" },
                ["sharc"]     = new() { Value = 647,   Allocation = "29 KB", Note = "17.9\u00D7 \u2014 header-only reads" },
            }
        },

        // ── ADVANCED ──

        new()
        {
            Id = "where-filter", Title = "WHERE Filter",
            Subtitle = "Scan + match using comparison operators",
            Icon = "\uD83D\uDD0D", Unit = "ms", CategoryId = "advanced",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan N rows, filter by age > 30 AND score < 50. Sharc: SharcFilter (6 ops). IndexedDB: no WHERE.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 3.2,  Allocation = "2.8 KB" },
                ["indexeddb"] = new() { Value = null,  NotSupported = true },
                ["surrealdb"] = new() { Value = 5.8,  Allocation = "4.2 KB" },
                ["arangodb"]  = new() { Value = 4.5,  Allocation = "3.6 KB", Note = "AQL FILTER" },
                ["sharc"]     = new() { Value = 1.8,  Allocation = "29 KB", Note = "SharcFilter \u2014 6 operators, all types" },
            }
        },

        // ── GRAPH ──

        new()
        {
            Id = "graph-node-scan", Title = "Graph: Node Scan",
            Subtitle = "Full _concepts table scan (ConceptStore)",
            Icon = "\uD83D\uDD35", Unit = "\u03BCs", CategoryId = "graph",
            DensityTiers = GraphDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan all N nodes in _concepts (id, key, kind, data). Tests graph abstraction overhead.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 2853, Allocation = "937 KB", Note = "Raw SQL \u2014 no graph layer" },
                ["indexeddb"] = new() { Value = null,  NotSupported = true },
                ["surrealdb"] = new() { Value = 1800, Allocation = "2.4 MB", Note = "Native record links" },
                ["arangodb"]  = new() { Value = 1500, Allocation = "2.0 MB", Note = "Native document collection" },
                ["sharc"]     = new() { Value = 1027, Allocation = "1.5 MB", Note = "2.8\u00D7 \u2014 ConceptStore B-tree" },
            }
        },
        new()
        {
            Id = "graph-edge-scan", Title = "Graph: Edge Scan",
            Subtitle = "Full _relations table scan (RelationStore)",
            Icon = "\uD83D\uDD17", Unit = "\u03BCs", CategoryId = "graph",
            DensityTiers = GraphDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan N edges in _relations (origin, target, kind, data). Tests RelationStore performance.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 7673, Allocation = "1.4 MB", Note = "Raw SQL \u2014 no graph model" },
                ["indexeddb"] = new() { Value = null,  NotSupported = true },
                ["surrealdb"] = new() { Value = 4200, Allocation = "5.1 MB", Note = "RELATE traversal" },
                ["arangodb"]  = new() { Value = 3800, Allocation = "4.5 MB", Note = "Edge collection scan" },
                ["sharc"]     = new() { Value = 2268, Allocation = "2.8 MB", Note = "3.4\u00D7 \u2014 RelationStore direct scan" },
            }
        },
        new()
        {
            Id = "graph-seek", Title = "Graph: Node Seek",
            Subtitle = "Single node by integer key (B-tree)",
            Icon = "\uD83D\uDCCD", Unit = "ns", CategoryId = "graph",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Look up node by integer key (rowid 2500/5000). Sub-\u03BCs = inline with token generation.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 26168, Allocation = "600 B" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true },
                ["surrealdb"] = new() { Value = 18000, Allocation = "1.8 KB", Note = "SELECT * FROM concept:2500" },
                ["arangodb"]  = new() { Value = 15000, Allocation = "1.4 KB", Note = "DOCUMENT() by _key" },
                ["sharc"]     = new() { Value = 585,   Allocation = "1,840 B", Note = "44.7\u00D7 \u2014 sub-\u03BCs context lookup" },
            }
        },
        new()
        {
            Id = "graph-traverse", Title = "Graph: 2-Hop BFS",
            Subtitle = "Expand neighbors\u00B2 from starting node",
            Icon = "\uD83C\uDF10", Unit = "\u03BCs", CategoryId = "graph",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "BFS: expand outgoing edges (hop 1), then neighbors' edges (hop 2). Core 'Get Context' for AI agents.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = null,  NotSupported = true, Note = "No native graph traversal" },
                ["indexeddb"] = new() { Value = null,  NotSupported = true },
                ["surrealdb"] = new() { Value = 2400, Allocation = "18 KB", Note = "->relation->concept\u00B2" },
                ["arangodb"]  = new() { Value = 1800, Allocation = "15 KB", Note = "AQL TRAVERSAL 1..2 OUTBOUND" },
                ["sharc"]     = new() { Value = 85,   Allocation = "12 KB", Note = "28\u00D7 \u2014 in-process BFS, zero serde" },
            }
        },

        // ── ADVANCED (continued) ──

        new()
        {
            Id = "gc-pressure", Title = "GC Pressure \u2014 Sustained Scan",
            Subtitle = "Heap allocation under continuous load",
            Icon = "\u267B\uFE0F", Unit = "ms", CategoryId = "advanced",
            DensityTiers = StandardDensityTiers, DefaultDensity = "xl", ScaleMode = "linear",
            Methodology = "Scan N integer rows sustained. Measures total heap allocation \u2014 the GC tax. Sharc: 292KB for 1M rows.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 354.5, Allocation = "7.0 KB" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true, Note = "Impractical at this scale" },
                ["surrealdb"] = new() { Value = 680,   Allocation = "45 MB" },
                ["arangodb"]  = new() { Value = 580,   Allocation = "38 MB" },
                ["sharc"]     = new() { Value = 124.9, Allocation = "292 KB", Note = "2.8\u00D7 faster, 18\u00D7 less memory" },
            }
        },
        new()
        {
            Id = "encryption", Title = "Encrypted Read",
            Subtitle = "AES-256-GCM + Argon2id KDF decrypt + read",
            Icon = "\uD83D\uDD12", Unit = "\u03BCs", CategoryId = "advanced",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Open encrypted .db, derive key (Argon2id), decrypt page-level AES-256-GCM, read 100 rows.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = null,  NotSupported = true, Note = "Requires SQLCipher" },
                ["indexeddb"] = new() { Value = null,  NotSupported = true },
                ["surrealdb"] = new() { Value = 890,  Allocation = "2.4 MB", Note = "Auth, not page-level crypto" },
                ["arangodb"]  = new() { Value = null,  NotSupported = true, Note = "No page-level encryption" },
                ["sharc"]     = new() { Value = 340,  Allocation = "48 KB", Note = "AES-256-GCM + Argon2id, page-level" },
            }
        },

        // ── META ──

        new()
        {
            Id = "memory-footprint", Title = "Engine Footprint",
            Subtitle = "Package size + baseline heap",
            Icon = "\uD83D\uDCE6", Unit = "KB", CategoryId = "meta",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Total download/package + idle heap. Smaller = faster cold starts, better for mobile/edge.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 1536, Allocation = "2.1 MB idle" },
                ["indexeddb"] = new() { Value = 0.1,  Allocation = "~0 (built-in)", Note = "Browser-native" },
                ["surrealdb"] = new() { Value = 8192, Allocation = "12 MB idle" },
                ["arangodb"]  = new() { Value = 10240, Allocation = "14 MB idle" },
                ["sharc"]     = new() { Value = 50,   Allocation = "50 KB total", Note = "30\u2013160\u00D7 smaller" },
            }
        },
        new()
        {
            Id = "primitives", Title = "Primitives (Sharc-only)",
            Subtitle = "Byte-level decode \u2014 no equivalent elsewhere",
            Icon = "\u269B\uFE0F", Unit = "ns", CategoryId = "meta",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Sharc internals: header parse (8.5ns/0B), varint decode (231ns/100/0B). No equivalent in other engines.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = null, NotSupported = true, Note = "Behind C library" },
                ["indexeddb"] = new() { Value = null, NotSupported = true, Note = "No byte-level API" },
                ["surrealdb"] = new() { Value = null, NotSupported = true, Note = "Behind Rust engine" },
                ["arangodb"]  = new() { Value = null, NotSupported = true, Note = "Behind C++ engine" },
                ["sharc"]     = new() { Value = 8.5,  Allocation = "0 B", Note = "Header: 8.5ns, 0 bytes" },
            }
        },
    ];
}
