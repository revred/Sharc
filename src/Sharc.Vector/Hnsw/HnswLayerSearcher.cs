// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Performs a single-layer beam search during HNSW graph construction.
/// </summary>
internal static class HnswLayerSearcher
{
    /// <summary>
    /// Beam search at a single layer; returns up to <paramref name="ef"/> nearest candidates.
    /// All distances are expected to be "lower is better".
    /// </summary>
    internal static List<(float Distance, int NodeIndex)> SearchLayer(
        HnswGraph graph,
        ReadOnlySpan<float> queryVector,
        int entryPoint,
        int ef,
        int layer,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn)
    {
        float entryDist = distanceFn(queryVector, resolver.GetVector(entryPoint));

        using var visited = new VisitedSet(graph.NodeCount);
        visited.Visit(entryPoint);

        var candidates = new CandidateHeap(graph.NodeCount, isMinHeap: true);
        var result = new CandidateHeap(ef + 1, isMinHeap: false);

        candidates.Push(entryDist, entryPoint);
        result.Push(entryDist, entryPoint);

        while (!candidates.IsEmpty)
        {
            var nearest = candidates.Pop();

            if (result.Count >= ef && nearest.Distance > result.PeekDistance())
                break;

            var neighbors = graph.GetNeighbors(layer, nearest.NodeIndex);
            for (int i = 0; i < neighbors.Length; i++)
            {
                int neighbor = neighbors[i];
                if (visited.IsVisited(neighbor))
                    continue;
                visited.Visit(neighbor);

                float dist = distanceFn(queryVector, resolver.GetVector(neighbor));
                if (result.Count < ef || dist < result.PeekDistance())
                {
                    candidates.Push(dist, neighbor);
                    result.Push(dist, neighbor);

                    if (result.Count > ef)
                        result.Pop();
                }
            }
        }

        var output = result.DrainToList();
        candidates.Dispose();
        result.Dispose();
        return output;
    }
}
