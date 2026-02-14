// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
    private readonly Queue<(NodeKey Key, int Depth, int PathParent)> _traversalQueue = new();
    private readonly List<TraversalNode> _resultNodesCache = new();

    // Parent-pointer path tracking: O(N) instead of O(N*D) list copies
    private readonly List<(NodeKey Key, int Parent)> _pathNodes = new();

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
    /// Disposes the graph context.
    /// </summary>
    public void Dispose()
    {
        _outgoingCursor?.Dispose();
        _incomingCursor?.Dispose();
        _concepts.Dispose();
        // RelationStore doesn't have Disposable cursors yet, but RelationStore itself doesn't need dispose.
        // Actually, stores should be disposed if they hold persistent cursors.
    }

    private static void EnsureInitialized()
    {
    }

    /// <inheritdoc/>
    public GraphRecord? GetNode(NodeKey key)
    {
        EnsureInitialized();
        return _concepts.Get(key);
    }

    /// <inheritdoc/>
    public GraphRecord? GetNode(RecordId id)
    {
        EnsureInitialized();
        return _concepts.Get(id.Key);
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kind = null)
    {
        EnsureInitialized();
        using var cursor = _relations.CreateEdgeCursor(origin, kind);
        while (cursor.MoveNext())
        {
            yield return MapToEdge(cursor);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge> GetIncomingEdges(NodeKey target, RelationKind? kind = null)
    {
        EnsureInitialized();
        using var cursor = _relations.CreateIncomingEdgeCursor(target, kind);
        while (cursor.MoveNext())
        {
            yield return MapToEdge(cursor);
        }
    }

    /// <inheritdoc/>
    public IEdgeCursor GetEdgeCursor(NodeKey origin, RelationKind? kind = null)
    {
        EnsureInitialized();
        return _relations.CreateEdgeCursor(origin, kind);
    }

    /// <inheritdoc/>
    public GraphResult Traverse(NodeKey startKey, TraversalPolicy policy)
    {
        EnsureInitialized();

        var resultNodes = _resultNodesCache;
        resultNodes.Clear();

        var visited = _visitedCache;
        visited.Clear();

        var queue = _traversalQueue;
        queue.Clear();

        bool trackPaths = policy.IncludePaths;
        var pathNodes = _pathNodes;
        pathNodes.Clear();

        int startPathIndex = -1;
        if (trackPaths)
        {
            startPathIndex = pathNodes.Count;
            pathNodes.Add((startKey, -1));
        }

        queue.Enqueue((startKey, 0, startPathIndex));
        visited.Add(startKey);

        RelationKind? filterKind = policy.Kind;

        if (_outgoingCursor == null && (policy.Direction == TraversalDirection.Outgoing || policy.Direction == TraversalDirection.Both))
            _outgoingCursor = _relations.CreateEdgeCursor(startKey, filterKind);

        if (_incomingCursor == null && (policy.Direction == TraversalDirection.Incoming || policy.Direction == TraversalDirection.Both))
            _incomingCursor = _relations.CreateIncomingEdgeCursor(startKey, filterKind);

        var outgoingCursor = _outgoingCursor;
        var incomingCursor = _incomingCursor;

        while (queue.Count > 0)
        {
            var (currentKey, depth, pathIndex) = queue.Dequeue();

            var record = _concepts.Get(currentKey, policy.IncludeData);
            if (record.HasValue)
            {
                IReadOnlyList<NodeKey>? path = trackPaths ? ReconstructPath(pathNodes, pathIndex) : null;
                resultNodes.Add(new TraversalNode(record.Value, depth, path));
            }

            if (currentKey == policy.StopAtKey && visited.Count > 1) break;

            if (policy.MaxDepth.HasValue && depth >= policy.MaxDepth.Value) continue;

            int fanOutCount = 0;

            if (outgoingCursor != null)
            {
                outgoingCursor.Reset(currentKey.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(outgoingCursor, false, depth, pathIndex);
            }

            if (incomingCursor != null)
            {
                incomingCursor.Reset(currentKey.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(incomingCursor, true, depth, pathIndex);
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
                            pathNodes.Add((nextKey, currentPathIndex));
                        }

                        queue.Enqueue((nextKey, currentDepth + 1, nextPathIndex));
                        fanOutCount++;
                    }
                }
            }
        }

        return new GraphResult(resultNodes);
    }

    private static List<NodeKey> ReconstructPath(List<(NodeKey Key, int Parent)> pathNodes, int index)
    {
        // Walk parent pointers to count depth, then fill in reverse
        int count = 0;
        int walk = index;
        while (walk >= 0)
        {
            count++;
            walk = pathNodes[walk].Parent;
        }

        var path = new List<NodeKey>(count);
        for (int i = 0; i < count; i++) path.Add(default);

        walk = index;
        for (int i = count - 1; i >= 0; i--)
        {
            path[i] = pathNodes[walk].Key;
            walk = pathNodes[walk].Parent;
        }

        return path;
    }

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
