// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Algorithms;

/// <summary>
/// Single-pass degree centrality computation. Scans outgoing and incoming
/// edge cursors once per node — no adjacency list materialization.
/// </summary>
internal static class DegreeCentralityComputer
{
    internal static IReadOnlyList<DegreeResult> Compute(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        Func<NodeKey, IEdgeCursor> incomingEdges,
        RelationKind? kind)
    {
        int? kindFilter = kind.HasValue ? (int)kind.Value : null;
        var results = new DegreeResult[nodes.Count];

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            int outDegree = CountEdges(outgoingEdges(node), kindFilter);
            int inDegree = CountEdges(incomingEdges(node), kindFilter);

            results[i] = new DegreeResult(node, inDegree, outDegree, inDegree + outDegree);
        }

        // Sort by TotalDegree descending (stable sort preserves original order for ties)
        Array.Sort(results, (a, b) => b.TotalDegree.CompareTo(a.TotalDegree));

        return results;
    }

    private static int CountEdges(IEdgeCursor cursor, int? kindFilter)
    {
        using (cursor)
        {
            int count = 0;
            while (cursor.MoveNext())
            {
                if (kindFilter == null || cursor.Kind == kindFilter.Value)
                    count++;
            }
            return count;
        }
    }
}
