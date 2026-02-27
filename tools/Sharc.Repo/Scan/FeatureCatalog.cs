// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo.Scan;

/// <summary>
/// Static catalog of known Sharc features mapped to source directory patterns.
/// Used by <see cref="CodebaseScanner"/> to auto-detect feature edges.
/// </summary>
public static class FeatureCatalog
{
    /// <summary>
    /// A feature definition with its name, description, source patterns, layer, and status.
    /// </summary>
    public readonly record struct FeatureDefinition(
        string Name,
        string Description,
        string Layer,
        string Status,
        string[] SourcePatterns,
        string[] TestPatterns,
        string[] Keywords);

    private static readonly FeatureDefinition[] Features =
    {
        // ── Core Engine ─────────────────────────────────────────────
        new("primitives",
            "Varint decoder and serial type codec",
            "core", "complete",
            ["src/Sharc.Core/Primitives/"],
            ["tests/Sharc.Tests/Primitives/", "tests/Sharc.Tests/Varint"],
            ["varint", "serial type"]),

        new("page-io",
            "Page I/O layer: file, memory, mmap, cache sources",
            "core", "complete",
            ["src/Sharc.Core/IO/"],
            ["tests/Sharc.Tests/IO/", "tests/Sharc.Tests/PageSource", "tests/Sharc.Tests/Cache"],
            ["page source", "file page", "memory page", "mmap", "cache"]),

        new("btree-read",
            "B-tree traversal, cursor, and cell parsing",
            "core", "complete",
            ["src/Sharc.Core/BTree/"],
            ["tests/Sharc.Tests/BTree/"],
            ["b-tree", "cursor", "cell parser", "btree"]),

        new("record-codec",
            "Record decoder/encoder for all serial types",
            "core", "complete",
            ["src/Sharc.Core/Records/"],
            ["tests/Sharc.Tests/Records/", "tests/Sharc.Tests/RecordDecoder"],
            ["record decoder", "record encoder", "column value"]),

        new("schema-reader",
            "sqlite_schema table parser",
            "core", "complete",
            ["src/Sharc.Core/Schema/"],
            ["tests/Sharc.Tests/Schema/"],
            ["schema reader", "sqlite_schema", "sqlite_master"]),

        new("header-parser",
            "Database and page header parsers",
            "core", "complete",
            ["src/Sharc.Core/Format/"],
            ["tests/Sharc.Tests/Format/", "tests/Sharc.Tests/Header"],
            ["database header", "page header", "magic string"]),

        // ── Public API ──────────────────────────────────────────────
        new("table-scans",
            "SharcDatabase + SharcDataReader public API",
            "api", "complete",
            ["src/Sharc/SharcDatabase", "src/Sharc/SharcDataReader", "src/Sharc/SharcDatabaseFactory"],
            ["tests/Sharc.Tests/SharcDatabase", "tests/Sharc.IntegrationTests/"],
            ["SharcDatabase", "SharcDataReader", "CreateReader"]),

        new("where-filter",
            "SharcFilter + FilterStar JIT-compiled WHERE filtering",
            "api", "complete",
            ["src/Sharc/Filter/", "src/Sharc.Core/Query/Filter"],
            ["tests/Sharc.Tests/Filter/"],
            ["SharcFilter", "FilterStar", "WHERE"]),

        new("dynamic-views",
            "Programmatic view registration and query",
            "api", "complete",
            ["src/Sharc/Views/"],
            ["tests/Sharc.Tests/Views/"],
            ["view", "ViewEngine", "ISqliteView"]),

        // ── Encryption ──────────────────────────────────────────────
        new("encryption",
            "AES-256-GCM page-level encryption with Argon2id KDF",
            "crypto", "complete",
            ["src/Sharc.Crypto/"],
            ["tests/Sharc.Tests/Crypto/", "tests/Sharc.Tests/Encryption"],
            ["encrypt", "AES", "Argon2", "KDF", "cipher"]),

        // ── Write Engine ────────────────────────────────────────────
        new("write-engine",
            "Full CRUD: INSERT/UPDATE/DELETE with B-tree splits, ACID transactions",
            "write", "complete",
            ["src/Sharc/Write/", "src/Sharc.Core/Write/"],
            ["tests/Sharc.Tests/Write/", "tests/Sharc.Tests/Transaction"],
            ["insert", "update", "delete", "transaction", "ACID", "write engine"]),

        new("freelist",
            "Free page tracking and recycling",
            "write", "complete",
            ["src/Sharc.Core/Storage/"],
            ["tests/Sharc.Tests/Storage/", "tests/Sharc.Tests/Freelist"],
            ["freelist", "free page", "vacuum"]),

        // ── Trust Layer ─────────────────────────────────────────────
        new("trust-layer",
            "Agent registry, ECDSA attestation, hash-chain ledger, reputation",
            "trust", "complete",
            ["src/Sharc/Trust/", "src/Sharc.Core/Trust/"],
            ["tests/Sharc.Tests/Trust/"],
            ["trust", "agent", "ledger", "attestation", "reputation", "ECDSA"]),

        new("entitlements",
            "Row-level and column-level access control",
            "trust", "complete",
            ["src/Sharc/Trust/RowLevel", "src/Sharc/Trust/Entitlement"],
            ["tests/Sharc.Tests/Trust/Entitlement", "tests/Sharc.Tests/Trust/RowLevel"],
            ["entitlement", "access control", "row-level"]),

        // ── Graph Engine ────────────────────────────────────────────
        new("graph-engine",
            "Graph storage: ConceptStore, RelationStore, GraphWriter",
            "graph", "complete",
            ["src/Sharc.Graph/"],
            ["tests/Sharc.Graph.Tests.Unit/"],
            ["graph", "concept", "relation", "BFS", "traversal"]),

        new("graph-surface",
            "Graph API surface models and interfaces",
            "graph", "complete",
            ["src/Sharc.Graph.Surface/"],
            ["tests/Sharc.Graph.Tests.Unit/Surface/"],
            ["graph surface", "GraphNode", "GraphEdge"]),

        // ── SQL Pipeline ────────────────────────────────────────────
        new("sql-pipeline",
            "SQL parser, compiler, executor: JOIN/GROUP BY/ORDER BY/UNION",
            "query", "complete",
            ["src/Sharc.Query/"],
            ["tests/Sharc.Query.Tests/"],
            ["SQL", "query", "join", "group by", "order by", "union"]),

        // ── Vector Search ───────────────────────────────────────────
        new("vector-search",
            "SIMD-accelerated HNSW vector similarity search",
            "vector", "complete",
            ["src/Sharc.Vector/"],
            ["tests/Sharc.Vector.Tests/"],
            ["vector", "HNSW", "similarity", "SIMD", "cosine"]),

        // ── Cross-Arc Sync ──────────────────────────────────────────
        new("cross-arc-sync",
            "ArcDiffer, FragmentSyncProtocol, FusedArcContext",
            "arc", "complete",
            ["src/Sharc.Arc/"],
            ["tests/Sharc.Arc.Tests/"],
            ["arc", "diff", "fragment", "sync", "fused"]),

        // ── Benchmarks ──────────────────────────────────────────────
        new("benchmarks",
            "BenchmarkDotNet core and comparison suites",
            "bench", "complete",
            ["bench/Sharc.Benchmarks/", "bench/Sharc.Comparisons/"],
            [],
            ["benchmark", "BenchmarkDotNet"]),

        new("browser-arena",
            "Blazor WASM 3-way benchmark arena",
            "bench", "complete",
            ["src/Sharc.Arena.Wasm/"],
            [],
            ["blazor", "wasm", "arena", "browser"]),

        // ── Tools ───────────────────────────────────────────────────
        new("archive-tool",
            "Conversation archiver with schema + sync protocol",
            "tools", "complete",
            ["tools/Sharc.Archive/"],
            ["tests/Sharc.Archive.Tests/"],
            ["archive", "conversation"]),

        new("repo-tool",
            "AI agent repository: annotations, decisions, MCP server",
            "tools", "complete",
            ["tools/Sharc.Repo/"],
            ["tests/Sharc.Repo.Tests/"],
            ["repo", "annotation", "decision", "MCP"]),

        new("context-server",
            "MCP Context Server for queries, benchmarks, tests",
            "tools", "complete",
            ["tools/Sharc.Context/"],
            ["tests/Sharc.Context.Tests/"],
            ["context", "MCP server"]),

        new("git-index",
            "GCD CLI: git history → Sharc database",
            "tools", "complete",
            ["tools/Sharc.Index/"],
            ["tests/Sharc.Index.Tests/"],
            ["git", "index", "GCD", "commit"]),
    };

    /// <summary>All known feature definitions.</summary>
    public static IReadOnlyList<FeatureDefinition> All => Features;

    /// <summary>
    /// Returns the feature definition for the given name, or null if not found.
    /// </summary>
    public static FeatureDefinition? GetFeature(string name)
    {
        foreach (ref readonly var f in Features.AsSpan())
        {
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    /// <summary>
    /// Returns all feature names whose source or test patterns match the given file path.
    /// Uses forward-slash normalized prefix matching.
    /// </summary>
    public static List<string> MatchFile(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var matches = new List<string>();

        foreach (ref readonly var f in Features.AsSpan())
        {
            if (MatchesPatterns(normalized, f.SourcePatterns) ||
                MatchesPatterns(normalized, f.TestPatterns))
            {
                matches.Add(f.Name);
            }
        }

        return matches;
    }

    private static bool MatchesPatterns(string path, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            // Pattern is a directory prefix (ending with /) or a file name prefix
            if (pattern.EndsWith('/'))
            {
                if (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // File name prefix match: "src/Sharc/SharcDatabase" matches "src/Sharc/SharcDatabase.cs"
                if (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
