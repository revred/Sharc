// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Graph.Algorithms;
using Sharc.Graph.Cypher;
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

    /// <summary>
    /// Computes PageRank scores for all nodes in the graph.
    /// Returns results sorted by score descending.
    /// </summary>
    /// <param name="options">Algorithm configuration (damping, epsilon, max iterations, kind filter).</param>
    public IReadOnlyList<NodeScore> PageRank(PageRankOptions options = default)
    {
        var keys = _concepts.FetchAllKeys();
        return GraphAlgorithms.PageRank(keys, k => _relations.CreateEdgeCursor(k), options);
    }

    /// <summary>
    /// Computes degree centrality for all nodes. Returns results sorted by total degree descending.
    /// </summary>
    /// <param name="kind">Optional edge kind filter.</param>
    public IReadOnlyList<DegreeResult> DegreeCentrality(RelationKind? kind = null)
    {
        var keys = _concepts.FetchAllKeys();
        return GraphAlgorithms.DegreeCentrality(
            keys,
            k => _relations.CreateEdgeCursor(k),
            k => _relations.CreateIncomingEdgeCursor(k),
            kind);
    }

    /// <summary>
    /// Returns nodes in topological order (dependency-first). Throws if a cycle is detected.
    /// </summary>
    /// <param name="kind">Optional edge kind filter.</param>
    /// <exception cref="InvalidOperationException">The graph contains a cycle.</exception>
    public IReadOnlyList<NodeKey> TopologicalSort(RelationKind? kind = null)
    {
        var keys = _concepts.FetchAllKeys();
        return GraphAlgorithms.TopologicalSort(keys, k => _relations.CreateEdgeCursor(k), kind);
    }

    /// <summary>
    /// Executes a Cypher query against this graph context.
    /// Compiles the query to a traversal or shortest path call.
    /// </summary>
    /// <param name="query">A Cypher MATCH statement.</param>
    /// <returns>The graph result from executing the query.</returns>
    /// <example>
    /// <code>
    /// var result = graph.Cypher("MATCH (a) |> [r:CALLS] |> (b) WHERE a.key = 42 RETURN b");
    /// var path = graph.Cypher("MATCH p = shortestPath((a) |> [*] |> (b)) WHERE a.key = 1 AND b.key = 99 RETURN p");
    /// </code>
    /// </example>
    public GraphResult Cypher(string query)
    {
        var parser = new CypherParser(query.AsSpan());
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);
        return CypherExecutor.Execute(plan, this);
    }

    /// <summary>
    /// Pre-compiles a Cypher query for repeated execution. Parse once, execute many times.
    /// The prepared statement captures the AST and plan — call <see cref="PreparedCypher.Execute()"/>
    /// or override start/end keys for parameterized execution.
    /// </summary>
    /// <param name="query">A Cypher MATCH statement.</param>
    /// <returns>A prepared Cypher query that can be executed repeatedly.</returns>
    /// <example>
    /// <code>
    /// var prepared = graph.PrepareCypher("MATCH (a) |> [r:CALLS*..3] |> (b) WHERE a.key = 1 RETURN b");
    /// var r1 = prepared.Execute();                    // uses compiled start key
    /// var r2 = prepared.Execute(new NodeKey(999));     // override start key
    /// </code>
    /// </example>
    public PreparedCypher PrepareCypher(string query)
    {
        var parser = new CypherParser(query.AsSpan());
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);
        return new PreparedCypher(plan, this);
    }

    /// <summary>
    /// Produces a prompt-ready <see cref="ContextSummary"/> by traversing outgoing edges
    /// from the given root key. Includes all reachable nodes up to <paramref name="maxDepth"/>
    /// hops, optionally capped at <paramref name="maxTokens"/> estimated tokens.
    /// </summary>
    /// <param name="root">The starting node key.</param>
    /// <param name="maxDepth">Maximum traversal depth (default 2).</param>
    /// <param name="maxTokens">Optional token budget. When set, records are included in BFS order
    /// until the cumulative token count would exceed this limit.</param>
    /// <returns>A <see cref="ContextSummary"/> with the included records and generated text.</returns>
    public ContextSummary GetContext(NodeKey root, int maxDepth = 2, int? maxTokens = null)
    {
        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = maxDepth,
            IncludeData = true
        };

        var result = Traverse(root, policy);

        if (result.Nodes.Count == 0)
            return new ContextSummary(Guid.Empty, string.Empty, 0, Array.Empty<GraphRecord>());

        var included = new List<GraphRecord>();
        int totalTokens = 0;

        foreach (var node in result.Nodes)
        {
            int nodeTokens = node.Record.Tokens > 0
                ? node.Record.Tokens
                : EstimateTokens(node.Record.JsonData);

            if (maxTokens.HasValue && totalTokens + nodeTokens > maxTokens.Value && included.Count > 0)
                break;

            included.Add(node.Record);
            totalTokens += nodeTokens;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var record in included)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(record.JsonData);
        }

        var rootRecord = included[0];
        Guid rootGuid = Guid.TryParse(rootRecord.Id.Id, out var parsed) ? parsed : Guid.Empty;

        return new ContextSummary(rootGuid, sb.ToString(), totalTokens, included);
    }

    /// <summary>
    /// Rough token estimate: ~4 characters per token for JSON payloads.
    /// </summary>
    private static int EstimateTokens(string text)
        => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    /// <summary>
    /// Finds the shortest path between two nodes using bidirectional BFS.
    /// Returns the sequence of <see cref="NodeKey"/> values from <paramref name="from"/> to <paramref name="to"/>,
    /// or <c>null</c> if no path exists within the policy constraints.
    /// </summary>
    /// <param name="from">The starting node key.</param>
    /// <param name="to">The destination node key.</param>
    /// <param name="policy">Optional traversal policy. Supports <see cref="TraversalPolicy.MaxDepth"/>,
    /// <see cref="TraversalPolicy.Kind"/>, <see cref="TraversalPolicy.MinWeight"/>, and
    /// <see cref="TraversalPolicy.Timeout"/>.</param>
    /// <returns>An ordered list of node keys representing the shortest path, or <c>null</c> if unreachable.</returns>
    public IReadOnlyList<NodeKey>? ShortestPath(NodeKey from, NodeKey to, TraversalPolicy policy = default)
    {
        // Same node — trivial path
        if (from == to)
            return [from];

        RelationKind? filterKind = policy.Kind;
        int? kindInt = filterKind.HasValue ? (int)filterKind.Value : null;

        // Forward frontier: expands outgoing from 'from'
        var forwardVisited = new Dictionary<NodeKey, NodeKey?>(64); // node → parent
        var forwardQueue = new Queue<(NodeKey Key, int Depth)>();
        forwardVisited[from] = null;
        forwardQueue.Enqueue((from, 0));

        // Backward frontier: expands incoming from 'to'
        var backwardVisited = new Dictionary<NodeKey, NodeKey?>(64); // node → parent (toward 'to')
        var backwardQueue = new Queue<(NodeKey Key, int Depth)>();
        backwardVisited[to] = null;
        backwardQueue.Enqueue((to, 0));

        // Cursors: forward uses outgoing, backward uses incoming
        using var forwardCursor = _relations.CreateEdgeCursor(from, filterKind);
        using var backwardCursor = _relations.CreateIncomingEdgeCursor(to, filterKind);

        // Timeout
        long? deadlineTicks = policy.Timeout.HasValue
            ? Stopwatch.GetTimestamp() + (long)(policy.Timeout.Value.TotalSeconds * Stopwatch.Frequency)
            : null;
        int iterCount = 0;

        // MaxDepth applies to total path length, so each side can expand up to maxDepth
        int maxForwardDepth = policy.MaxDepth ?? int.MaxValue;
        int maxBackwardDepth = policy.MaxDepth ?? int.MaxValue;

        NodeKey? meetingNode = null;

        while (forwardQueue.Count > 0 || backwardQueue.Count > 0)
        {
            // Check timeout every 64 iterations
            if (deadlineTicks.HasValue && (++iterCount & 63) == 0
                && Stopwatch.GetTimestamp() >= deadlineTicks.Value)
                break;

            // Expand forward frontier (one level)
            if (forwardQueue.Count > 0)
            {
                var (fKey, fDepth) = forwardQueue.Dequeue();
                if (fDepth < maxForwardDepth)
                {
                    forwardCursor.Reset(fKey.Value, kindInt);
                    while (forwardCursor.MoveNext())
                    {
                        if (policy.MinWeight.HasValue && forwardCursor.Weight < policy.MinWeight.Value)
                            continue;

                        var nextKey = new NodeKey(forwardCursor.TargetKey);
                        if (!forwardVisited.ContainsKey(nextKey))
                        {
                            forwardVisited[nextKey] = fKey;
                            forwardQueue.Enqueue((nextKey, fDepth + 1));

                            // Check if backward frontier already visited this node
                            if (backwardVisited.ContainsKey(nextKey))
                            {
                                meetingNode = nextKey;
                                goto PathFound;
                            }
                        }
                    }
                }
            }

            // Expand backward frontier (one level)
            if (backwardQueue.Count > 0)
            {
                var (bKey, bDepth) = backwardQueue.Dequeue();
                if (bDepth < maxBackwardDepth)
                {
                    backwardCursor.Reset(bKey.Value, kindInt);
                    while (backwardCursor.MoveNext())
                    {
                        if (policy.MinWeight.HasValue && backwardCursor.Weight < policy.MinWeight.Value)
                            continue;

                        // Incoming cursor: OriginKey is the neighbor (the node pointing toward bKey)
                        var nextKey = new NodeKey(backwardCursor.OriginKey);
                        if (!backwardVisited.ContainsKey(nextKey))
                        {
                            backwardVisited[nextKey] = bKey;
                            backwardQueue.Enqueue((nextKey, bDepth + 1));

                            // Check if forward frontier already visited this node
                            if (forwardVisited.ContainsKey(nextKey))
                            {
                                meetingNode = nextKey;
                                goto PathFound;
                            }
                        }
                    }
                }
            }
        }

        return null; // No path found

    PathFound:
        var shortPath = ReconstructBidirectionalPath(forwardVisited, backwardVisited, meetingNode!.Value);

        // Validate total path length against MaxDepth (edges = nodes - 1)
        if (policy.MaxDepth.HasValue && shortPath.Count - 1 > policy.MaxDepth.Value)
            return null;

        return shortPath;
    }

    /// <summary>
    /// Reconstructs the full path from bidirectional BFS parent maps and the meeting node.
    /// </summary>
    private static List<NodeKey> ReconstructBidirectionalPath(
        Dictionary<NodeKey, NodeKey?> forwardVisited,
        Dictionary<NodeKey, NodeKey?> backwardVisited,
        NodeKey meeting)
    {
        // Build forward half: from → ... → meeting
        var forwardHalf = new List<NodeKey>();
        NodeKey? current = meeting;
        while (current.HasValue)
        {
            forwardHalf.Add(current.Value);
            current = forwardVisited[current.Value];
        }
        forwardHalf.Reverse(); // Now: from → ... → meeting

        // Build backward half: meeting → ... → to
        current = backwardVisited[meeting];
        while (current.HasValue)
        {
            forwardHalf.Add(current.Value);
            current = backwardVisited[current.Value];
        }

        return forwardHalf;
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
