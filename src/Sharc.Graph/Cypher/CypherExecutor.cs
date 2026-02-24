// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Cypher;

/// <summary>
/// Executes a compiled <see cref="CypherPlan"/> against a <see cref="SharcContextGraph"/>.
/// Maps to existing Traverse() and ShortestPath() methods.
/// </summary>
internal static class CypherExecutor
{
    internal static GraphResult Execute(CypherPlan plan, SharcContextGraph graph)
    {
        if (plan.IsShortestPath)
        {
            return ExecuteShortestPath(plan, graph);
        }

        return ExecuteTraversal(plan, graph);
    }

    private static GraphResult ExecuteTraversal(CypherPlan plan, SharcContextGraph graph)
    {
        if (!plan.StartKey.HasValue)
            throw new InvalidOperationException(
                "Cypher MATCH requires a WHERE clause with start node key (e.g., WHERE a.key = 42).");

        return graph.Traverse(plan.StartKey.Value, plan.Policy);
    }

    private static GraphResult ExecuteShortestPath(CypherPlan plan, SharcContextGraph graph)
    {
        if (!plan.StartKey.HasValue || !plan.EndKey.HasValue)
            throw new InvalidOperationException(
                "shortestPath requires WHERE constraints for both start and end keys.");

        var path = graph.ShortestPath(plan.StartKey.Value, plan.EndKey.Value, plan.Policy);
        if (path == null)
            return new GraphResult(Array.Empty<TraversalNode>());

        // Convert path keys to traversal nodes
        var nodes = new List<TraversalNode>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            var record = graph.GetNode(path[i]);
            if (record.HasValue)
                nodes.Add(new TraversalNode(record.Value, i, path));
        }

        return new GraphResult(nodes);
    }
}
