// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Graph.Store;

namespace Sharc.Graph;

/// <summary>
/// High-performance graph context for navigating semantic networks.
/// Optimized for zero-allocation traversals and SQLite-backed persistence.
/// </summary>
public sealed class SharcContextGraph : IContextGraph, IDisposable
{
    private readonly ConceptStore _concepts;
    private readonly RelationStore _relations;
    private readonly ISchemaAdapter _schema;
    private readonly Dictionary<int, string> _typeNameCache = new();
    private IEdgeCursor? _outgoingCursor;
    private IEdgeCursor? _incomingCursor;
    
    // Persistent collections for zero-allocation reuse
    private readonly HashSet<NodeKey> _visitedCache = new();
    private readonly Queue<TraversalQueueItem> _traversalQueue = new();
    private readonly List<TraversalNode> _resultNodesCache = new();

    // Two-phase BFS: collected keys from Phase 1 (edge traversal), resolved in Phase 2 (node lookup)
    private readonly List<TraversalQueueItem> _collectedKeys = new();

    // Parent-pointer path tracking: O(N) instead of O(N*D) list copies
    private readonly List<PathReconstructionNode> _pathNodes = new();

    /// <summary>
    /// Creates a new graph context from an existing b-tree reader.
    /// Used for standalone benchmarks and advanced integrations.
    /// </summary>
    /// <param name="reader">The underlying B-tree reader.</param>
    /// <param name="schema">The schema mapping adapter.</param>
    public SharcContextGraph(IBTreeReader reader, ISchemaAdapter schema)
    {
        _concepts = new ConceptStore(reader, schema);
        _relations = new RelationStore(reader, schema);
        _schema = schema;
    }

    internal SharcContextGraph(ConceptStore concepts, RelationStore relations, ISchemaAdapter schema)
    {
        _concepts = concepts;
        _relations = relations;
        _schema = schema;
    }

    /// <summary>
    /// Initializes the graph stores by reading the database schema.
    /// Must be called before traversal if not using the high-level database entry point.
    /// </summary>
    public void Initialize()
    {
        // Standalone initialization for benchmarks/tests
        var decoder = new RecordDecoder();
        var schemaReader = new SchemaReader(_concepts.Reader, decoder);
        var schema = schemaReader.ReadSchema();

        _concepts.Initialize(schema);
        _relations.Initialize(schema);
    }

    /// <summary>
    /// Initializes the graph stores with a pre-built schema. Used by unit tests with fake readers.
    /// </summary>
    internal void Initialize(SharcSchema schema)
    {
        _concepts.Initialize(schema);
        _relations.Initialize(schema);
    }

    /// <summary>
    /// Disposes the graph context and its cached cursors.
    /// </summary>
    public void Dispose()
    {
        _outgoingCursor?.Dispose();
        _incomingCursor?.Dispose();
        _concepts.Dispose();
        // RelationStore does not hold persistent cursors — nothing to dispose.
    }

    /// <inheritdoc/>
    public GraphRecord? GetNode(NodeKey key)
    {
        return _concepts.Get(key);
    }

    /// <inheritdoc/>
    public GraphRecord? GetNode(RecordId id)
    {
        return _concepts.Get(id.Key);
    }

    /// <inheritdoc/>
    [Obsolete("Allocates per edge. Use GetEdgeCursor() for zero-alloc access or Traverse() for BFS.")]
    public IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kind = null)
    {
        using var cursor = _relations.CreateEdgeCursor(origin, kind);
        while (cursor.MoveNext())
        {
            yield return MapToEdge(cursor);
        }
    }

    /// <inheritdoc/>
    [Obsolete("Allocates per edge. Use Traverse() with Direction=Incoming for zero-alloc BFS.")]
    public IEnumerable<GraphEdge> GetIncomingEdges(NodeKey target, RelationKind? kind = null)
    {
        using var cursor = _relations.CreateIncomingEdgeCursor(target, kind);
        while (cursor.MoveNext())
        {
            yield return MapToEdge(cursor);
        }
    }

    /// <inheritdoc/>
    public IEdgeCursor GetEdgeCursor(NodeKey origin, RelationKind? kind = null)
    {
        return _relations.CreateEdgeCursor(origin, kind);
    }

    /// <summary>
    /// Creates a <see cref="PreparedTraversal"/> that owns independent cursors and state,
    /// enabling concurrent traversals from the same graph context. The policy is captured
    /// at prepare time and reused on each <see cref="PreparedTraversal.Execute"/> call.
    /// </summary>
    /// <param name="policy">The traversal policy (direction, depth, fan-out, etc.).</param>
    /// <returns>A <see cref="PreparedTraversal"/> handle. Call <see cref="PreparedTraversal.Execute"/> to run.</returns>
    public PreparedTraversal PrepareTraversal(TraversalPolicy policy)
    {
        return new PreparedTraversal(_concepts, _relations, policy);
    }

    /// <inheritdoc/>
    public GraphResult Traverse(NodeKey startKey, TraversalPolicy policy)
    {
        var resultNodes = _resultNodesCache;
        resultNodes.Clear();

        var visited = _visitedCache;
        visited.Clear();

        var queue = _traversalQueue;
        queue.Clear();

        var collectedKeys = _collectedKeys;
        collectedKeys.Clear();

        bool trackPaths = policy.IncludePaths;
        var pathNodes = _pathNodes;
        pathNodes.Clear();

        // Pre-size collections based on policy hints to avoid resizing
        int capacityHint = EstimateCapacity(policy);
        visited.EnsureCapacity(capacityHint);

        int startPathIndex = -1;
        if (trackPaths)
        {
            startPathIndex = pathNodes.Count;
            pathNodes.Add(new PathReconstructionNode(startKey, -1));
        }

        queue.Enqueue(new TraversalQueueItem(startKey, 0, startPathIndex));
        visited.Add(startKey);

        RelationKind? filterKind = policy.Kind;

        if (_outgoingCursor == null && (policy.Direction == TraversalDirection.Outgoing || policy.Direction == TraversalDirection.Both))
            _outgoingCursor = _relations.CreateEdgeCursor(startKey, filterKind);

        if (_incomingCursor == null && (policy.Direction == TraversalDirection.Incoming || policy.Direction == TraversalDirection.Both))
            _incomingCursor = _relations.CreateIncomingEdgeCursor(startKey, filterKind);

        var outgoingCursor = _outgoingCursor;
        var incomingCursor = _incomingCursor;

        // Timeout: compute deadline once, check periodically
        long? deadlineTicks = policy.Timeout.HasValue
            ? Stopwatch.GetTimestamp() + (long)(policy.Timeout.Value.TotalSeconds * Stopwatch.Frequency)
            : null;
        int iterCount = 0;

        // ── Phase 1: Edge-only BFS ──
        // Discover reachable node set using edge cursors only.
        // No concept lookups here — keeps page cache on the relation B-tree.
        bool stoppedEarly = false;
        while (queue.Count > 0)
        {
            // Check timeout every 64 iterations to avoid Stopwatch overhead
            if (deadlineTicks.HasValue && (++iterCount & 63) == 0
                && Stopwatch.GetTimestamp() >= deadlineTicks.Value)
                break;

            var item = queue.Dequeue();
            collectedKeys.Add(item);

            if (item.Key == policy.StopAtKey && visited.Count > 1) { stoppedEarly = true; break; }

            if (policy.MaxDepth.HasValue && item.Depth >= policy.MaxDepth.Value) continue;

            int fanOutCount = 0;

            if (outgoingCursor != null)
            {
                outgoingCursor.Reset(item.Key.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(outgoingCursor, false, item.Depth, item.PathIndex);
            }

            if (incomingCursor != null)
            {
                incomingCursor.Reset(item.Key.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(incomingCursor, true, item.Depth, item.PathIndex);
            }

            void ProcessCursor(IEdgeCursor cursor, bool isIncoming, int currentDepth, int currentPathIndex)
            {
                while (cursor.MoveNext())
                {
                    if (policy.MinWeight.HasValue && cursor.Weight < policy.MinWeight.Value) continue;
                    if (policy.MaxFanOut.HasValue && fanOutCount >= policy.MaxFanOut.Value) break;

                    var nextKey = new NodeKey(isIncoming ? cursor.OriginKey : cursor.TargetKey);
                    if (visited.Add(nextKey))
                    {
                        int nextPathIndex = -1;
                        if (trackPaths)
                        {
                            nextPathIndex = pathNodes.Count;
                            pathNodes.Add(new PathReconstructionNode(nextKey, currentPathIndex));
                        }

                        queue.Enqueue(new TraversalQueueItem(nextKey, currentDepth + 1, nextPathIndex));
                        fanOutCount++;
                    }
                }
            }
        }

        // If stopped early (StopAtKey), also collect remaining queued nodes
        // so they appear in results (they were discovered but not yet dequeued)
        if (!stoppedEarly)
        {
            while (queue.Count > 0)
                collectedKeys.Add(queue.Dequeue());
        }

        // ── Phase 2: Batch node lookup ──
        // All concept B-tree lookups happen sequentially — better page cache locality.
        int tokenBudget = policy.MaxTokens ?? int.MaxValue;
        int tokensUsed = 0;
        bool hasTypeFilter = policy.TargetTypeFilter.HasValue;
        int targetType = policy.TargetTypeFilter.GetValueOrDefault();

        foreach (var item in collectedKeys)
        {
            var record = _concepts.Get(item.Key, policy.IncludeData);
            if (!record.HasValue) continue;

            // TargetTypeFilter: skip nodes of the wrong type
            if (hasTypeFilter && record.Value.TypeId != targetType) continue;

            // MaxTokens: stop when token budget is exhausted (BFS order prioritizes closer nodes)
            if (policy.MaxTokens.HasValue)
            {
                tokensUsed += record.Value.Tokens;
                if (tokensUsed > tokenBudget) break;
            }

            IReadOnlyList<NodeKey>? path = trackPaths ? ReconstructPath(pathNodes, item.PathIndex) : null;
            resultNodes.Add(new TraversalNode(record.Value, item.Depth, path));
        }

        return new GraphResult(resultNodes);
    }

    private static int EstimateCapacity(TraversalPolicy policy)
    {
        if (policy.MaxDepth.HasValue && policy.MaxFanOut.HasValue)
        {
            int estimate = 1;
            for (int d = 0; d < policy.MaxDepth.Value && estimate < 4096; d++)
                estimate *= policy.MaxFanOut.Value;
            return Math.Min(estimate, 4096);
        }
        return 128;
    }

    private static List<NodeKey> ReconstructPath(List<PathReconstructionNode> pathNodes, int index)
    {
        // Walk parent pointers to count depth, then fill in reverse
        int count = 0;
        int walk = index;
        while (walk >= 0)
        {
            count++;
            walk = pathNodes[walk].ParentIndex;
        }

        var path = new List<NodeKey>(count);
        for (int i = 0; i < count; i++) path.Add(default);

        walk = index;
        for (int i = count - 1; i >= 0; i--)
        {
            path[i] = pathNodes[walk].Key;
            walk = pathNodes[walk].ParentIndex;
        }

        return path;
    }

    // ── Obsolete support code: only used by deprecated GetEdges/GetIncomingEdges ──

    [Obsolete("Only used by deprecated GetEdges/GetIncomingEdges.")]
    private string GetTypeName(int kind)
    {
        if (_typeNameCache.TryGetValue(kind, out var name)) return name;
        if (_schema.TypeNames.TryGetValue(kind, out name))
        {
            _typeNameCache[kind] = name;
            return name;
        }
        name = kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _typeNameCache[kind] = name;
        return name;
    }

    [Obsolete("Only used by deprecated GetEdges/GetIncomingEdges. Use cursor properties directly.")]
    private GraphEdge MapToEdge(IEdgeCursor cursor)
    {
        long originKey = cursor.OriginKey;
        long targetKey = cursor.TargetKey;
        int kind = cursor.Kind;
        string typeStr = GetTypeName(kind);

        string edgeId = string.Create(null, stackalloc char[64], $"pk:{originKey}->{targetKey}");

        return new GraphEdge(
            new RecordId(typeStr, edgeId, new NodeKey(originKey)),
            new NodeKey(originKey),
            new NodeKey(targetKey),
            kind,
            cursor.JsonDataUtf8.Length > 0 ? System.Text.Encoding.UTF8.GetString(cursor.JsonDataUtf8.Span) : "{}"
        )
        {
            KindName = typeStr,
            Weight = cursor.Weight
        };
    }
}
