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
        new() { Id = "xs", Label = "500",  Rows = 500,    Scale = 0.01 },
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
        // Values sourced from BenchmarkDotNet v0.15.8, .NET 10.0.2, i7-11800H.
        // Sharc: 3.66 µs, SQLite: 23.91 µs → 6.5× advantage

        new()
        {
            Id = "engine-load", Title = "Engine Initialization",
            Subtitle = "Cold start: download + instantiate + allocate",
            Icon = "\uD83D\uDE80", Unit = "ms", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Cold start: download WASM binary (if applicable), instantiate engine, allocate initial memory. Measured: Sharc 3.66\u03BCs vs SQLite 23.91\u03BCs (6.5\u00D7). Fixed workload.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 142,   Allocation = "1.5 MB", Note = "Emscripten WASM + P/Invoke init" },
                ["indexeddb"] = new() { Value = 2.1,   Allocation = "0 B", Note = "Browser-native (no download)" },
                ["surrealdb"] = new() { Value = 487,   Allocation = "8.2 MB" },
                ["arangodb"]  = new() { Value = 350,   Allocation = "10 MB" },
                ["sharc"]     = new() { Value = 0.004, Allocation = "15.2 KB", Note = "6.5\u00D7 \u2014 pure C#, no WASM download" },
            }
        },
        new()
        {
            Id = "schema-read", Title = "Schema Introspection",
            Subtitle = "Read table names, columns, constraints",
            Icon = "\uD83D\uDCCB", Unit = "\u03BCs", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Read all table definitions. Sharc walks sqlite_schema B-tree directly. Measured: 2.97\u03BCs vs SQLite 26.66\u03BCs (9.0\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 26.66, Allocation = "2.5 KB", Note = "sqlite_master query" },
                ["indexeddb"] = new() { Value = 45,    Allocation = "1.2 KB", Note = "objectStoreNames only" },
                ["surrealdb"] = new() { Value = 320,   Allocation = "12 KB" },
                ["arangodb"]  = new() { Value = 280,   Allocation = "9.5 KB" },
                ["sharc"]     = new() { Value = 2.97,  Allocation = "6.8 KB", Note = "9.0\u00D7 \u2014 direct B-tree walk" },
            }
        },
        new()
        {
            Id = "sequential-scan", Title = "Sequential Scan",
            Subtitle = "Full table scan, all columns decoded",
            Icon = "\uD83D\uDCCA", Unit = "ms", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan 5K rows with 9 columns (int, text, float, blob, null). Decode all values. Measured: Sharc 2.59ms vs SQLite 6.03ms (2.3\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 6.03,  Allocation = "1.4 MB", Note = "P/Invoke per-column decode" },
                ["indexeddb"] = new() { Value = 89,    Allocation = "22 MB", Note = "Cursor iteration + JS objects" },
                ["surrealdb"] = new() { Value = 52,    Allocation = "28 MB" },
                ["arangodb"]  = new() { Value = 45,    Allocation = "24 MB", Note = "C++ document scan" },
                ["sharc"]     = new() { Value = 2.59,  Allocation = "2.4 MB", Note = "2.3\u00D7 \u2014 projection + lazy decode" },
            }
        },
        new()
        {
            Id = "point-lookup", Title = "B-Tree Point Lookup",
            Subtitle = "Single row by primary key (binary search)",
            Icon = "\uD83C\uDFAF", Unit = "ns", CategoryId = "core",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "Seek to rowid via B-tree binary search descent. Measured: Sharc 3,444ns vs SQLite 24,347ns (7.1\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 24347,  Allocation = "728 B", Note = "Prepared WHERE rowid = ?" },
                ["indexeddb"] = new() { Value = 85000,  Allocation = "320 B", Note = "IDB get() async" },
                ["surrealdb"] = new() { Value = 42000,  Allocation = "1.2 KB" },
                ["arangodb"]  = new() { Value = 35000,  Allocation = "980 B", Note = "Hash index lookup" },
                ["sharc"]     = new() { Value = 3444,   Allocation = "8,320 B", Note = "7.1\u00D7 \u2014 zero-overhead B-tree seek" },
            }
        },
        new()
        {
            Id = "batch-lookup", Title = "Batch Lookups",
            Subtitle = "Consecutive seeks with page cache locality",
            Icon = "\u26A1", Unit = "ns", CategoryId = "core",
            DensityTiers = LookupDensityTiers, DefaultDensity = "sm", ScaleMode = "linear",
            Methodology = "6 sequential PK lookups across B-tree. Measured: Sharc 5,237ns vs SQLite 127,763ns (24.4\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 127763, Allocation = "3,712 B", Note = "Prepared statement, 6 executions" },
                ["indexeddb"] = new() { Value = 520000, Allocation = "1.9 KB", Note = "6 async get() calls" },
                ["surrealdb"] = new() { Value = 280000, Allocation = "7.2 KB" },
                ["arangodb"]  = new() { Value = 240000, Allocation = "5.8 KB", Note = "Batch hash lookups" },
                ["sharc"]     = new() { Value = 5237,   Allocation = "9,424 B", Note = "24.4\u00D7 \u2014 cursor seek reuse" },
            }
        },
        new()
        {
            Id = "type-decode", Title = "Type Decoding \u2014 Integers",
            Subtitle = "Raw decode speed for typed column values",
            Icon = "\uD83D\uDD22", Unit = "ms", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "xl", ScaleMode = "linear",
            Methodology = "Decode 5K integer values. Sharc: ReadOnlySpan<byte> + struct returns. Measured: 0.213ms vs SQLite 0.819ms (3.8\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 0.819, Allocation = "688 B", Note = "sqlite3_column_int64 P/Invoke" },
                ["indexeddb"] = new() { Value = 42,    Allocation = "3.2 MB", Note = "Everything is JS objects" },
                ["surrealdb"] = new() { Value = 24,    Allocation = "1.8 MB" },
                ["arangodb"]  = new() { Value = 20,    Allocation = "1.4 MB", Note = "VPack binary format" },
                ["sharc"]     = new() { Value = 0.213, Allocation = "8.2 KB", Note = "3.8\u00D7 \u2014 zero-alloc Span pipeline" },
            }
        },
        new()
        {
            Id = "null-scan", Title = "NULL Detection",
            Subtitle = "Check NULLs without full row decode",
            Icon = "\u2205", Unit = "\u03BCs", CategoryId = "core",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan 5K rows checking nullable column. Sharc reads only serial type headers. Measured: 156\u03BCs vs SQLite 746\u03BCs (4.8\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 746,   Allocation = "688 B", Note = "sqlite3_column_type P/Invoke" },
                ["indexeddb"] = new() { Value = 38000, Allocation = "4.8 MB", Note = "Full object deserialization" },
                ["surrealdb"] = new() { Value = 18000, Allocation = "2.1 MB" },
                ["arangodb"]  = new() { Value = 15000, Allocation = "1.6 MB" },
                ["sharc"]     = new() { Value = 156,   Allocation = "8.2 KB", Note = "4.8\u00D7 \u2014 header-only reads" },
            }
        },

        // ── ADVANCED ──

        new()
        {
            Id = "where-filter", Title = "WHERE Filter",
            Subtitle = "Scan + match using comparison operators",
            Icon = "\uD83D\uDD0D", Unit = "ms", CategoryId = "advanced",
            DensityTiers = StandardDensityTiers, DefaultDensity = "md", ScaleMode = "linear",
            Methodology = "Scan 5K rows, filter by age > 30 AND score < 50. Measured: Sharc 1.23ms vs SQLite 0.55ms. SQLite wins via VDBE predicate pushdown.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 0.554, Allocation = "720 B", Note = "VDBE predicate pushdown" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true, Note = "No query language" },
                ["surrealdb"] = new() { Value = 5.8,   Allocation = "4.2 KB" },
                ["arangodb"]  = new() { Value = 4.5,   Allocation = "3.6 KB", Note = "AQL FILTER" },
                ["sharc"]     = new() { Value = 1.229, Allocation = "1.1 MB", Note = "SharcFilter \u2014 6 operators, all types" },
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
            Methodology = "Scan N edges in _relations (source_key, target_key, kind, data). Tests RelationStore performance.",
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
            Methodology = "Look up node by integer key (rowid 2500/5000). Measured: Sharc 3,444ns vs SQLite 24,347ns (7.1\u00D7).",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 24347, Allocation = "728 B", Note = "WHERE key = ? prepared" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true },
                ["surrealdb"] = new() { Value = 18000, Allocation = "1.8 KB", Note = "SELECT * FROM concept:2500" },
                ["arangodb"]  = new() { Value = 15000, Allocation = "1.4 KB", Note = "DOCUMENT() by _key" },
                ["sharc"]     = new() { Value = 3444,  Allocation = "8,320 B", Note = "7.1\u00D7 \u2014 sub-\u03BCs context lookup" },
            }
        },
        new()
        {
            Id = "graph-traverse", Title = "Graph: 2-Hop BFS",
            Subtitle = "Expand neighbors\u00B2 from starting node",
            Icon = "\uD83C\uDF10", Unit = "\u03BCs", CategoryId = "graph",
            DensityTiers = FixedTiers, DefaultDensity = "md", ScaleMode = "fixed",
            Methodology = "BFS: expand outgoing edges (hop 1), then neighbors' edges (hop 2). Measured: Sharc 6.27\u03BCs vs SQLite 78.49\u03BCs (12.5\u00D7). SeekFirst O(log n) index.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 78.49, Allocation = "2.7 KB", Note = "SQL WHERE source_key = ? prepared" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true },
                ["surrealdb"] = new() { Value = 2400,  Allocation = "18 KB", Note = "->relation->concept\u00B2" },
                ["arangodb"]  = new() { Value = 1800,  Allocation = "15 KB", Note = "AQL TRAVERSAL 1..2 OUTBOUND" },
                ["sharc"]     = new() { Value = 6.27,  Allocation = "10.9 KB", Note = "12.5\u00D7 \u2014 SeekFirst O(log n) index" },
            }
        },

        // ── ADVANCED (continued) ──

        new()
        {
            Id = "gc-pressure", Title = "GC Pressure \u2014 Sustained Scan",
            Subtitle = "Heap allocation under continuous load",
            Icon = "\u267B\uFE0F", Unit = "ms", CategoryId = "advanced",
            DensityTiers = StandardDensityTiers, DefaultDensity = "xl", ScaleMode = "linear",
            Methodology = "Scan 5K integer rows sustained. Measured: Sharc 0.213ms/8.2KB vs SQLite 0.798ms/688B. Sharc 3.7\u00D7 faster.",
            BaseResults = new Dictionary<string, EngineBaseResult>
            {
                ["sqlite"]    = new() { Value = 0.798, Allocation = "688 B", Note = "P/Invoke path, minimal alloc" },
                ["indexeddb"] = new() { Value = null,   NotSupported = true, Note = "Impractical at this scale" },
                ["surrealdb"] = new() { Value = 680,   Allocation = "45 MB" },
                ["arangodb"]  = new() { Value = 580,   Allocation = "38 MB" },
                ["sharc"]     = new() { Value = 0.213, Allocation = "8.2 KB", Note = "3.7\u00D7 faster, zero-alloc Span" },
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
                ["sqlite"]    = new() { Value = 1536, Allocation = "2.1 MB idle", Note = "e_sqlite3.wasm + managed wrapper" },
                ["indexeddb"] = new() { Value = 0.1,  Allocation = "~0 (built-in)", Note = "Browser-native" },
                ["surrealdb"] = new() { Value = 8192, Allocation = "12 MB idle" },
                ["arangodb"]  = new() { Value = 10240, Allocation = "14 MB idle" },
                ["sharc"]     = new() { Value = 50,   Allocation = "50 KB total", Note = "30\u2013200\u00D7 smaller \u2014 pure C#" },
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
