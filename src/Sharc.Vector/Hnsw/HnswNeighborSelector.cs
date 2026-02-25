// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Neighbor selection strategies for HNSW graph construction.
/// </summary>
internal static class HnswNeighborSelector
{
    /// <summary>
    /// Simple selection: take the M nearest candidates by distance (ascending).
    /// </summary>
    internal static int[] SelectSimple(
        Span<(float Distance, int NodeIndex)> candidates,
        int maxConnections)
    {
        // Sort by distance ascending
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        int count = Math.Min(candidates.Length, maxConnections);
        var result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = candidates[i].NodeIndex;
        return result;
    }

    /// <summary>
    /// Heuristic selection (Algorithm 4 from HNSW paper): prefer diverse neighbors.
    /// A candidate is added only if it is closer to the target than to any already-selected neighbor.
    /// This produces a more uniform spatial coverage of the neighborhood, improving recall.
    /// </summary>
    internal static int[] SelectHeuristic(
        ReadOnlySpan<float> targetVector,
        Span<(float Distance, int NodeIndex)> candidates,
        int maxConnections,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn)
    {
        // Sort candidates by distance to target (ascending — nearest first)
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var selected = new List<int>(maxConnections);

        for (int i = 0; i < candidates.Length && selected.Count < maxConnections; i++)
        {
            var candidate = candidates[i];
            bool isGood = true;

            // Check if candidate is closer to target than to any already-selected neighbor
            var candidateVector = resolver.GetVector(candidate.NodeIndex);
            for (int j = 0; j < selected.Count; j++)
            {
                var selectedVector = resolver.GetVector(selected[j]);
                float distToSelected = distanceFn(candidateVector, selectedVector);

                if (distToSelected < candidate.Distance)
                {
                    // Candidate is closer to an already-selected neighbor than to target.
                    // Skip it — adding it would create a redundant connection.
                    isGood = false;
                    break;
                }
            }

            if (isGood)
                selected.Add(candidate.NodeIndex);
        }

        return selected.ToArray();
    }
}
