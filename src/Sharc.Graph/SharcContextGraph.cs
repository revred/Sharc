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
    private readonly Queue<(NodeKey Key, int Depth, List<NodeKey>? Path)> _traversalQueue = new();
    private readonly List<TraversalNode> _resultNodesCache = new();

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
        var list = new List<GraphEdge>();
        using var cursor = _relations.CreateEdgeCursor(origin, kind);
        while (cursor.MoveNext())
        {
            list.Add(MapToEdge(cursor));
        }
        return list;
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge> GetIncomingEdges(NodeKey target, RelationKind? kind = null)
    {
        EnsureInitialized();
        var list = new List<GraphEdge>();
        using var cursor = _relations.CreateIncomingEdgeCursor(target, kind);
        while (cursor.MoveNext())
        {
            list.Add(MapToEdge(cursor));
        }
        return list;
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

        List<NodeKey>? startPath = policy.IncludePaths ? new List<NodeKey> { startKey } : null;
        queue.Enqueue((startKey, 0, startPath));
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
            var (currentKey, depth, path) = queue.Dequeue();

            // Materialize record only if requested or if we are returning it
            var record = _concepts.Get(currentKey, policy.IncludeData);
            if (record.HasValue)
            {
                resultNodes.Add(new TraversalNode(record.Value, depth, path));
            }

            if (currentKey == policy.StopAtKey && visited.Count > 1) break;

            if (policy.MaxDepth.HasValue && depth >= policy.MaxDepth.Value) continue;

            int fanOutCount = 0;

            if (outgoingCursor != null)
            {
                outgoingCursor.Reset(currentKey.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(outgoingCursor, false, depth, path);
            }

            if (incomingCursor != null)
            {
                incomingCursor.Reset(currentKey.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(incomingCursor, true, depth, path);
            }

            void ProcessCursor(IEdgeCursor cursor, bool isIncoming, int currentDepth, List<NodeKey>? currentPath)
            {
                while (cursor.MoveNext())
                {
                    if (policy.MinWeight.HasValue && cursor.Weight < policy.MinWeight.Value) continue;
                    if (policy.MaxFanOut.HasValue && fanOutCount >= policy.MaxFanOut.Value) break;

                    var nextKey = new NodeKey(isIncoming ? cursor.OriginKey : cursor.TargetKey);
                    if (visited.Add(nextKey))
                    {
                        List<NodeKey>? nextPath = null;
                        if (policy.IncludePaths && currentPath != null)
                        {
                            nextPath = new List<NodeKey>(currentPath) { nextKey };
                        }
                        
                        queue.Enqueue((nextKey, currentDepth + 1, nextPath));
                        fanOutCount++;
                    }
                }
            }
        }

        return new GraphResult(resultNodes);
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

        return new GraphEdge(
            new RecordId(typeStr, "pk:" + originKey + "->" + targetKey, new NodeKey(originKey)),
            new NodeKey(originKey),
            new NodeKey(targetKey),
            kind,
            cursor.JsonDataUtf8.Length > 0 ? System.Text.Encoding.UTF8.GetString(cursor.JsonDataUtf8.Span) : "{}"
        )
        {
            KindName = typeStr,
            CVN = 1,
            LVN = 1,
            SyncStatus = 0,
            Weight = cursor.Weight,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
