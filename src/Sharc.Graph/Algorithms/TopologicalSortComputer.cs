// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Algorithms;

/// <summary>
/// DFS post-order topological sort with cycle detection.
/// Builds adjacency internally (unavoidable for back-edge detection).
/// </summary>
internal static class TopologicalSortComputer
{
    internal static IReadOnlyList<NodeKey> Compute(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        RelationKind? kind)
    {
        int? kindFilter = kind.HasValue ? (int)kind.Value : null;

        // Build adjacency lists and node-index mapping
        var nodeIndex = new Dictionary<long, int>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
            nodeIndex[nodes[i].Value] = i;

        var adjacency = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            adjacency[i] = new List<int>();
            using var cursor = outgoingEdges(nodes[i]);
            while (cursor.MoveNext())
            {
                if (kindFilter != null && cursor.Kind != kindFilter.Value)
                    continue;

                if (nodeIndex.TryGetValue(cursor.TargetKey, out int targetIdx))
                    adjacency[i].Add(targetIdx);
            }
        }

        // DFS with three-color marking: 0=white, 1=gray (in stack), 2=black (done)
        var color = new byte[nodes.Count];
        var postOrder = new List<int>(nodes.Count);

        for (int i = 0; i < nodes.Count; i++)
        {
            if (color[i] == 0)
                DfsIterative(i, adjacency, color, postOrder);
        }

        // Post-order DFS gives reverse topological order
        postOrder.Reverse();

        var result = new NodeKey[postOrder.Count];
        for (int i = 0; i < postOrder.Count; i++)
            result[i] = nodes[postOrder[i]];

        return result;
    }

    private static void DfsIterative(
        int start,
        List<int>[] adjacency,
        byte[] color,
        List<int> postOrder)
    {
        // Explicit stack: (nodeIndex, neighborCursor). When neighborCursor equals
        // adjacency[node].Count, all neighbors are visited and we can finalize.
        var stack = new Stack<(int Node, int NeighborIdx)>();

        color[start] = 1; // gray
        stack.Push((start, 0));

        while (stack.Count > 0)
        {
            var (node, nIdx) = stack.Pop();
            var neighbors = adjacency[node];

            bool descended = false;
            for (int i = nIdx; i < neighbors.Count; i++)
            {
                int neighbor = neighbors[i];
                if (color[neighbor] == 1)
                    throw new InvalidOperationException(
                        "Graph contains a cycle — topological sort is not possible.");

                if (color[neighbor] == 0)
                {
                    // Save progress on current node, descend into neighbor
                    stack.Push((node, i + 1));
                    color[neighbor] = 1; // gray
                    stack.Push((neighbor, 0));
                    descended = true;
                    break;
                }
            }

            if (!descended)
            {
                // All neighbors visited — mark black and record post-order
                color[node] = 2;
                postOrder.Add(node);
            }
        }
    }
}
