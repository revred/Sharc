// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Linq;
using Sharc.Core;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Graph.Store;
using Sharc.Core.Schema;

namespace Sharc.Graph;

/// <summary>
/// Concrete implementation of <see cref="IContextGraph"/> that coordinates
/// concept and relation stores.
/// </summary>
public sealed class SharcContextGraph : IContextGraph, IDisposable
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private ConceptStore _concepts;
    private RelationStore _relations;
    private bool _initialized;

    /// <summary>
    /// Creates a new SharcContextGraph.
    /// </summary>
    /// <param name="reader">The B-Tree reader.</param>
    /// <param name="schema">The schema adapter.</param>
    public SharcContextGraph(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _concepts = new ConceptStore(reader, schema);
        _relations = new RelationStore(reader, schema);
    }

    /// <summary>
    /// Initializes the graph by loading schema and setting up stores.
    /// </summary>
    public void Initialize(SharcSchema? schema = null)
    {
        if (_initialized) return;

        var schemaInfo = schema ?? new SchemaLoader(_reader).Load();

        _concepts = new ConceptStore(_reader, _schema);
        _concepts.Initialize(schemaInfo);

        _relations = new RelationStore(_reader, _schema);
        _relations.Initialize(schemaInfo);

        _initialized = true;
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
        // ConceptStore currently only supports lookup by Key (integer).
        // If the ID has a key, use it.
        if (id.HasIntegerKey)
        {
            return _concepts.Get(id.Key);
        }
        
        return _concepts.Get(id.Id);
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kind = null)
    {
        EnsureInitialized();
        return _relations.GetEdges(origin, kind);
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge> GetIncomingEdges(NodeKey target, RelationKind? kind = null)
    {
        EnsureInitialized();
        return _relations.GetIncomingEdges(target, kind);
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

        var resultNodes = new List<TraversalNode>();
        var visited = new HashSet<NodeKey>();
        var queue = new Queue<(NodeKey Key, int Depth, List<NodeKey> Path)>();
        int totalTokens = 0;

        var startPath = new List<NodeKey> { startKey };
        queue.Enqueue((startKey, 0, startPath));
        visited.Add(startKey);

        while (queue.Count > 0)
        {
            var (currentKey, depth, currentPath) = queue.Dequeue();
            var record = _concepts.Get(currentKey);

            if (record != null)
            {
                // Token Budgeting
                if (policy.MaxTokens.HasValue && totalTokens + record.Tokens > policy.MaxTokens.Value)
                {
                    // If we can't fit this entire node, we stop expansion here for this branch
                    if (resultNodes.Count > 0) break;
                    // Special case: if even the first node exceeds budget, we still return it but stop.
                }

                resultNodes.Add(new TraversalNode(record, depth, currentPath));
                totalTokens += record.Tokens;
            }

            if (currentKey == policy.StopAtKey && visited.Count > 1) break;

            // Follow edges
            if (policy.MaxDepth.HasValue && depth >= policy.MaxDepth.Value) continue;

            int count = 0;

            // Define which getters to use based on direction
            bool includeOutgoing = policy.Direction == TraversalDirection.Outgoing || policy.Direction == TraversalDirection.Both;
            bool includeIncoming = policy.Direction == TraversalDirection.Incoming || policy.Direction == TraversalDirection.Both;

            if (includeOutgoing)
            {
                ProcessEdges(_relations.GetEdges(currentKey), false);
            }

            if (includeIncoming)
            {
                ProcessEdges(_relations.GetIncomingEdges(currentKey), true);
            }

            void ProcessEdges(IEnumerable<GraphEdge> edges, bool isIncoming)
            {
                if (policy.MaxTokens.HasValue)
                {
                    // Budget active — sort and materializing
                    var sortedEdges = edges.OrderByDescending(e => e.Weight);

                    foreach (var edge in sortedEdges)
                    {
                        if (policy.MinWeight.HasValue && edge.Weight < policy.MinWeight.Value) continue;
                        if (policy.MaxFanOut.HasValue && count >= policy.MaxFanOut.Value) break; // Shared fan-out count

                        var nextKey = isIncoming ? edge.OriginKey : edge.TargetKey;
                        if (visited.Add(nextKey))
                        {
                            var nextPath = new List<NodeKey>(currentPath) { nextKey };
                            queue.Enqueue((nextKey, depth + 1, nextPath));
                            count++;
                        }
                    }
                }
                else
                {
                    // No budget — use zero-alloc cursor if possible, but we are using IEnumerable here for shared logic.
                    // To optimize, we should use cursors directly.
                    // For now, sticking to IEnumerable logic for Bidirectional simplicity, 
                    // but calling the Store methods which might use IndexScan.
                    
                    // Note: If optimal perf is needed, we should manually use CreateEdgeCursor/CreateIncomingEdgeCursor
                    // inside this method to avoid enumerator allocations.
                    // But GetEdges returns IEnumerable, so we are already allocating the enumerator.
                    
                    foreach (var edge in edges)
                    {
                        if (policy.MinWeight.HasValue && edge.Weight < policy.MinWeight.Value) continue;
                        if (policy.MaxFanOut.HasValue && count >= policy.MaxFanOut.Value) break;

                        var nextKey = isIncoming ? edge.OriginKey : edge.TargetKey;
                        if (visited.Add(nextKey))
                        {
                            var nextPath = new List<NodeKey>(currentPath) { nextKey };
                            queue.Enqueue((nextKey, depth + 1, nextPath));
                            count++;
                        }
                    }
                }
            }
        }

        return new GraphResult(resultNodes);
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Graph not initialized. Call Initialize() first.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // IBTreeReader is not IDisposable. The owner of the reader is responsible for cleanup.
    }
}