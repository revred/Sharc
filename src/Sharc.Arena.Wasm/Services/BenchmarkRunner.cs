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

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Orchestrates live benchmark execution across all engines.
///
/// Tier 1 (same .NET WASM runtime, Stopwatch + GC alloc tracking):
///   - Sharc:  pure C# format reader
///   - SQLite: Microsoft.Data.Sqlite (C → Emscripten → P/Invoke)
///
/// Tier 2 (browser API, JS interop, performance.now() timing):
///   - IndexedDB: browser-native key-value store
///
/// SurrealDB: reference data only (MVP — live engine deferred).
/// </summary>
public sealed class BenchmarkRunner : IBenchmarkEngine
{
    private readonly SharcEngine _sharcEngine;
    private readonly SqliteEngine _sqliteEngine;
    private readonly IndexedDbEngine _indexedDbEngine;
    private readonly ReferenceEngine _referenceEngine;
    private readonly DataGenerator _dataGenerator = new();

    private byte[]? _dbBytes;
    private int _lastUserCount;
    private int _lastNodeCount;

    public BenchmarkRunner(
        SharcEngine sharcEngine,
        SqliteEngine sqliteEngine,
        IndexedDbEngine indexedDbEngine,
        ReferenceEngine referenceEngine)
    {
        _sharcEngine = sharcEngine;
        _sqliteEngine = sqliteEngine;
        _indexedDbEngine = indexedDbEngine;
        _referenceEngine = referenceEngine;
    }

    public async Task<IReadOnlyDictionary<string, EngineBaseResult>> RunSlideAsync(
        SlideDefinition slide, double scale, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Runner] Running slide: {slide.Id} (scale: {scale})");

        // Get reference results (used for SurrealDB which stays on reference data)
        var referenceResults = await _referenceEngine.RunSlideAsync(slide, scale, cancellationToken);

        // Calculate row counts from scale
        var userCount = ScaleToUserCount(slide, scale);
        var nodeCount = ScaleToNodeCount(slide, scale);

        Console.WriteLine($"[Runner] Initializing engines with {userCount} users, {nodeCount} nodes");
        await EnsureAllEnginesInitialized(userCount, nodeCount);

        // Run Tier 1 engines (sync, same .NET runtime)
        var sharcResult = RunSharcSlide(slide.Id, scale);
        var sqliteResult = RunSqliteSlide(slide.Id, scale);

        // Run Tier 2 engine (async, JS interop)
        var indexedDbResult = await _indexedDbEngine.RunSlide(slide.Id, scale);

        // Merge: live results for sharc/sqlite/indexeddb, reference for surrealdb
        var merged = new Dictionary<string, EngineBaseResult>(referenceResults.Count);
        foreach (var (engineId, result) in referenceResults)
        {
            merged[engineId] = engineId switch
            {
                "sharc" => sharcResult,
                "sqlite" => sqliteResult,
                "indexeddb" => indexedDbResult,
                _ => result, // surrealdb stays on reference data
            };
        }

        // Yield to avoid blocking the UI thread
        await Task.Yield();
        return merged;
    }

    /// <summary>
    /// Generates the canonical database byte[] ONCE and shares it across all engines.
    /// Eliminates 3× redundant DataGenerator runs (was: each engine generated its own copy).
    /// </summary>
    private async Task EnsureAllEnginesInitialized(int userCount, int nodeCount)
    {
        if (userCount != _lastUserCount || nodeCount != _lastNodeCount)
        {
            // Single generation — deterministic seed=42, identical for all engines
            _dbBytes = _dataGenerator.GenerateDatabase(userCount, nodeCount);

            _sharcEngine.Reset();
            _sharcEngine.EnsureInitialized(_dbBytes);

            _sqliteEngine.Reset();
            _sqliteEngine.EnsureInitialized(_dbBytes);

            _lastUserCount = userCount;
            _lastNodeCount = nodeCount;
        }

        await _indexedDbEngine.EnsureInitialized(_dbBytes!, userCount, nodeCount);
    }

    private EngineBaseResult RunSharcSlide(string slideId, double scale) =>
        slideId switch
        {
            "engine-load"      => _sharcEngine.RunEngineLoad(),
            "schema-read"      => _sharcEngine.RunSchemaRead(),
            "sequential-scan"  => _sharcEngine.RunSequentialScan(scale),
            "point-lookup"     => _sharcEngine.RunPointLookup(),
            "batch-lookup"     => _sharcEngine.RunBatchLookup(scale),
            "type-decode"      => _sharcEngine.RunTypeDecode(scale),
            "null-scan"        => _sharcEngine.RunNullScan(scale),
            "where-filter"     => _sharcEngine.RunWhereFilter(scale),
            "graph-node-scan"  => _sharcEngine.RunGraphNodeScan(scale),
            "graph-edge-scan"  => _sharcEngine.RunGraphEdgeScan(scale),
            "graph-seek"       => _sharcEngine.RunGraphSeek(),
            "graph-traverse"   => _sharcEngine.RunGraphTraverse(),
            "gc-pressure"      => _sharcEngine.RunGcPressure(scale),
            "encryption"       => _sharcEngine.RunEncryption(),
            "memory-footprint" => _sharcEngine.RunMemoryFootprint(),
            "primitives"       => _sharcEngine.RunPrimitives(),
            _                  => new EngineBaseResult { Value = null, Note = "Unknown slide" },
        };

    private EngineBaseResult RunSqliteSlide(string slideId, double scale) =>
        slideId switch
        {
            "engine-load"      => _sqliteEngine.RunEngineLoad(),
            "schema-read"      => _sqliteEngine.RunSchemaRead(),
            "sequential-scan"  => _sqliteEngine.RunSequentialScan(scale),
            "point-lookup"     => _sqliteEngine.RunPointLookup(),
            "batch-lookup"     => _sqliteEngine.RunBatchLookup(scale),
            "type-decode"      => _sqliteEngine.RunTypeDecode(scale),
            "null-scan"        => _sqliteEngine.RunNullScan(scale),
            "where-filter"     => _sqliteEngine.RunWhereFilter(scale),
            "graph-node-scan"  => _sqliteEngine.RunGraphNodeScan(scale),
            "graph-edge-scan"  => _sqliteEngine.RunGraphEdgeScan(scale),
            "graph-seek"       => _sqliteEngine.RunGraphSeek(),
            "graph-traverse"   => _sqliteEngine.RunGraphTraverse(),
            "gc-pressure"      => _sqliteEngine.RunGcPressure(scale),
            "encryption"       => _sqliteEngine.RunEncryption(),
            "memory-footprint" => _sqliteEngine.RunMemoryFootprint(),
            "primitives"       => _sqliteEngine.RunPrimitives(),
            _                  => new EngineBaseResult { Value = null, Note = "Unknown slide" },
        };

    private static int ScaleToUserCount(SlideDefinition slide, double scale)
    {
        // Find the density tier matching this scale to get row count
        foreach (var tier in slide.DensityTiers)
        {
            if (Math.Abs(tier.Scale - scale) < 0.001)
                return Math.Max(100, tier.Rows);
        }
        // Fallback: estimate from scale
        return Math.Max(100, (int)(5000 * scale));
    }

    private static int ScaleToNodeCount(SlideDefinition slide, double scale)
    {
        // Graph slides use GraphDensityTiers; others default to 1/5 of user count
        if (slide.CategoryId == "graph")
        {
            foreach (var tier in slide.DensityTiers)
            {
                if (Math.Abs(tier.Scale - scale) < 0.001)
                    return Math.Max(50, tier.Rows);
            }
            return Math.Max(50, (int)(5000 * scale));
        }
        return Math.Max(50, (int)(1000 * scale));
    }
}
