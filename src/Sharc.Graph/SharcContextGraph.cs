/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using System.Linq;
using Sharc.Core;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Graph.Store;

namespace Sharc.Graph;

/// <summary>
/// Concrete implementation of <see cref="IContextGraph"/> that coordinates
/// concept and relation stores.
/// </summary>
public sealed class SharcContextGraph : IContextGraph, IDisposable
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private readonly ConceptStore _concepts;
    private readonly RelationStore _relations;
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
    /// Initializes the graph by loading the database schema.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        var loader = new SchemaLoader(_reader);
        var schemaInfo = loader.Load();

        _concepts.Initialize(schemaInfo);
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

            // Follow edges (currently only Outgoing is supported efficiently)
            if (policy.Direction == TraversalDirection.Outgoing)
            {
                // Depth limit
                if (policy.MaxDepth.HasValue && depth >= policy.MaxDepth.Value) continue;

                int count = 0;

                if (policy.MaxTokens.HasValue)
                {
                    // Budget active — need to sort by weight, must materialize edges
                    var edges = _relations.GetEdges(currentKey)
                        .OrderByDescending(e => e.Weight);

                    foreach (var edge in edges)
                    {
                        if (policy.MinWeight.HasValue && edge.Weight < policy.MinWeight.Value) continue;
                        if (policy.MaxFanOut.HasValue && count >= policy.MaxFanOut.Value) break;

                        if (!visited.Contains(edge.TargetKey))
                        {
                            visited.Add(edge.TargetKey);
                            var nextPath = new List<NodeKey>(currentPath) { edge.TargetKey };
                            queue.Enqueue((edge.TargetKey, depth + 1, nextPath));
                            count++;
                        }
                    }
                }
                else
                {
                    // No budget — use zero-alloc edge cursor (no GraphEdge allocation per row)
                    using var cursor = _relations.CreateEdgeCursor(currentKey);
                    while (cursor.MoveNext())
                    {
                        if (policy.MinWeight.HasValue && cursor.Weight < policy.MinWeight.Value) continue;
                        if (policy.MaxFanOut.HasValue && count >= policy.MaxFanOut.Value) break;

                        var targetKey = new NodeKey(cursor.TargetKey);
                        if (visited.Add(targetKey))
                        {
                            var nextPath = new List<NodeKey>(currentPath) { targetKey };
                            queue.Enqueue((targetKey, depth + 1, nextPath));
                            count++;
                        }
                    }
                }
            }
            // Incoming traversal requires full table scan in reverse or index planned for M7
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
