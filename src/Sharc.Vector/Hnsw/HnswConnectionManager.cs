// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Updates reverse connections and performs neighbor pruning when capacity is exceeded.
/// </summary>
internal static class HnswConnectionManager
{
    internal static void AddConnection(
        HnswGraph graph,
        int layer,
        int sourceNode,
        int targetNode,
        int maxConnections,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn,
        HnswConfig config)
    {
        var existing = graph.GetNeighborArray(layer, sourceNode);
        int existingCount = existing?.Length ?? 0;

        if (existingCount < maxConnections)
        {
            var newNeighbors = new int[existingCount + 1];
            if (existing != null)
                Array.Copy(existing, newNeighbors, existingCount);
            newNeighbors[existingCount] = targetNode;
            graph.SetNeighbors(layer, sourceNode, newNeighbors);
            return;
        }

        var sourceVector = resolver.GetVector(sourceNode);
        var allCandidates = new (float Distance, int NodeIndex)[existingCount + 1];
        for (int i = 0; i < existingCount; i++)
        {
            float d = distanceFn(sourceVector, resolver.GetVector(existing![i]));
            allCandidates[i] = (d, existing[i]);
        }
        allCandidates[existingCount] =
            (distanceFn(sourceVector, resolver.GetVector(targetNode)), targetNode);

        int[] pruned = config.UseHeuristic
            ? HnswNeighborSelector.SelectHeuristic(
                sourceVector, allCandidates.AsSpan(), maxConnections, resolver, distanceFn)
            : HnswNeighborSelector.SelectSimple(allCandidates.AsSpan(), maxConnections);

        graph.SetNeighbors(layer, sourceNode, pruned);
    }
}
