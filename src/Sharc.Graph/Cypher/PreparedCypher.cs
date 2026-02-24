// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Cypher;

/// <summary>
/// A pre-compiled Cypher query that captures the AST and plan at prepare time.
/// Execute many times with different parameters via closure binding.
/// This is the "prepared statement" pattern — parse once, execute many.
/// </summary>
public sealed class PreparedCypher
{
    private readonly CypherPlan _plan;
    private readonly SharcContextGraph _graph;

    internal PreparedCypher(CypherPlan plan, SharcContextGraph graph)
    {
        _plan = plan;
        _graph = graph;
    }

    /// <summary>
    /// Executes the prepared Cypher query with the compiled plan.
    /// </summary>
    public GraphResult Execute() => CypherExecutor.Execute(_plan, _graph);

    /// <summary>
    /// Executes with an overridden start key (re-binding the WHERE constraint).
    /// </summary>
    public GraphResult Execute(NodeKey startKey)
    {
        _plan.StartKey = startKey;
        return CypherExecutor.Execute(_plan, _graph);
    }

    /// <summary>
    /// Executes with overridden start and end keys (for shortestPath).
    /// </summary>
    public GraphResult Execute(NodeKey startKey, NodeKey endKey)
    {
        _plan.StartKey = startKey;
        _plan.EndKey = endKey;
        return CypherExecutor.Execute(_plan, _graph);
    }
}
