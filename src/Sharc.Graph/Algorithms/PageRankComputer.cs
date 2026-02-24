// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Algorithms;

/// <summary>
/// Iterative power-method PageRank. Builds adjacency internally (unavoidable
/// for iterative convergence — must traverse edges many times).
/// </summary>
internal static class PageRankComputer
{
    internal static IReadOnlyList<NodeScore> Compute(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        PageRankOptions options)
    {
        // Use defaults if the struct was default-initialized (all zeros)
        double damping = options.DampingFactor > 0 ? options.DampingFactor : 0.85;
        double epsilon = options.Epsilon > 0 ? options.Epsilon : 1e-6;
        int maxIter = options.MaxIterations > 0 ? options.MaxIterations : 100;
        int? kindFilter = options.Kind.HasValue ? (int)options.Kind.Value : null;

        int n = nodes.Count;

        // Build node-index mapping
        var nodeIndex = new Dictionary<long, int>(n);
        for (int i = 0; i < n; i++)
            nodeIndex[nodes[i].Value] = i;

        // Build adjacency: outEdges[i] = list of target indices
        var outEdges = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            outEdges[i] = new List<int>();
            using var cursor = outgoingEdges(nodes[i]);
            while (cursor.MoveNext())
            {
                if (kindFilter != null && cursor.Kind != kindFilter.Value)
                    continue;

                if (nodeIndex.TryGetValue(cursor.TargetKey, out int targetIdx))
                    outEdges[i].Add(targetIdx);
            }
        }

        // Initialize scores uniformly
        double[] scores = new double[n];
        double[] newScores = new double[n];
        double initial = 1.0 / n;
        for (int i = 0; i < n; i++)
            scores[i] = initial;

        double teleport = (1.0 - damping) / n;

        // Power iteration
        for (int iter = 0; iter < maxIter; iter++)
        {
            // Reset new scores to teleport value
            for (int i = 0; i < n; i++)
                newScores[i] = teleport;

            // Distribute rank from each node to its neighbors
            for (int i = 0; i < n; i++)
            {
                var neighbors = outEdges[i];
                if (neighbors.Count == 0)
                {
                    // Dangling node — distribute rank equally to all nodes
                    double share = damping * scores[i] / n;
                    for (int j = 0; j < n; j++)
                        newScores[j] += share;
                }
                else
                {
                    double share = damping * scores[i] / neighbors.Count;
                    for (int j = 0; j < neighbors.Count; j++)
                        newScores[neighbors[j]] += share;
                }
            }

            // Check convergence
            double maxDelta = 0;
            for (int i = 0; i < n; i++)
            {
                double delta = Math.Abs(newScores[i] - scores[i]);
                if (delta > maxDelta) maxDelta = delta;
            }

            // Swap buffers
            (scores, newScores) = (newScores, scores);

            if (maxDelta < epsilon)
                break;
        }

        // Build results sorted by score descending
        var result = new NodeScore[n];
        for (int i = 0; i < n; i++)
            result[i] = new NodeScore(nodes[i], scores[i]);

        Array.Sort(result, (a, b) => b.Score.CompareTo(a.Score));

        return result;
    }
}
