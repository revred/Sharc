// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Sharc.Views;

namespace Sharc.Graph.Query;

/// <summary>
/// An <see cref="ILayer"/> that executes a graph traversal lazily when opened.
/// Register via <c>db.RegisterLayer(layer)</c>, then reference the layer name
/// in SQL queries: <c>SELECT * FROM [reachable] WHERE depth &lt;= 2</c>.
/// </summary>
public sealed class GraphTraversalLayer : ILayer
{
    private readonly SharcContextGraph _graph;
    private readonly NodeKey _startKey;
    private readonly TraversalPolicy _policy;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public MaterializationStrategy Strategy => MaterializationStrategy.Eager;

    /// <summary>
    /// Creates a new graph traversal layer.
    /// </summary>
    /// <param name="name">The layer name (referenced in SQL queries).</param>
    /// <param name="graph">The graph context to traverse.</param>
    /// <param name="startKey">The starting node key for traversal.</param>
    /// <param name="policy">The traversal policy.</param>
    public GraphTraversalLayer(string name, SharcContextGraph graph, NodeKey startKey, TraversalPolicy policy)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _graph = graph;
        _startKey = startKey;
        _policy = policy;
    }

    /// <inheritdoc/>
    public IViewCursor Open(SharcDatabase db)
    {
        if (_graph == null)
            throw new InvalidOperationException("Graph context was not provided.");
        var result = _graph.Traverse(_startKey, _policy);
        return new GraphTraversalCursor(result.Nodes);
    }
}
