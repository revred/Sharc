// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// HNSW search algorithm. Implements the multi-layer greedy descent + beam search
/// from the HNSW paper (Algorithm 5).
/// </summary>
internal static class HnswGraphSearcher
{
    /// <summary>
    /// Searches the HNSW graph for the k nearest neighbors to the query vector.
    /// </summary>
    /// <param name="graph">The HNSW graph.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <param name="ef">Beam width for search (higher = better recall, slower).</param>
    /// <param name="resolver">Resolves node indices to vectors.</param>
    /// <param name="distanceFn">Distance computation function.</param>
    /// <param name="metric">Distance metric (for determining sort order).</param>
    /// <returns>Search results ordered by relevance.</returns>
    internal static VectorSearchResult Search(
        HnswGraph graph,
        ReadOnlySpan<float> queryVector,
        int k,
        int ef,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn,
        DistanceMetric metric)
    {
        if (graph.NodeCount == 0)
            return new VectorSearchResult(new List<VectorMatch>());

        // Clamp ef to at least k
        ef = Math.Max(ef, k);

        // For DotProduct, negate scores so all internals use "lower is better".
        // This avoids duplicating comparison logic throughout the algorithm.
        bool isDotProduct = metric == DistanceMetric.DotProduct;
        VectorDistanceFunction internalFn = isDotProduct
            ? (a, b) => -distanceFn(a, b)
            : distanceFn;

        int entryPoint = graph.EntryPoint;
        int maxLevel = graph.MaxLevel;

        // Phase 1: Greedy descent from entry point through layers maxLevel..1
        float entryDist = internalFn(queryVector, resolver.GetVector(entryPoint));
        int closest = entryPoint;
        float closestDist = entryDist;

        for (int layer = maxLevel; layer >= 1; layer--)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var neighbors = graph.GetNeighbors(layer, closest);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int neighbor = neighbors[i];
                    float dist = internalFn(queryVector, resolver.GetVector(neighbor));
                    if (dist < closestDist)
                    {
                        closest = neighbor;
                        closestDist = dist;
                        changed = true;
                    }
                }
            }
        }

        // Phase 2: Beam search at layer 0 with ef candidates
        var results = BeamSearchLayer0(graph, queryVector, closest, ef, resolver, internalFn);

        // Phase 3: Extract top-K (always min-heap since internal scores are "lower is better")
        var heap = new VectorTopKHeap(k, isMinHeap: true);

        for (int i = 0; i < results.Count; i++)
        {
            long rowId = graph.GetRowId(results[i].NodeIndex);
            // Keep negated distances in heap so min-heap selects the best candidates
            heap.TryInsert(rowId, results[i].Distance);
        }

        if (isDotProduct)
        {
            // Negate distances back and sort descending (higher = more similar)
            return heap.ToResultNegatedDescending();
        }
        return heap.ToResult();
    }

    /// <summary>
    /// Beam search at layer 0 — the main search workhorse.
    /// Uses pooled CandidateHeap + VisitedSet for zero tree/hash allocation.
    /// All distances are assumed "lower is better" (DotProduct negated by caller).
    /// </summary>
    private static List<(float Distance, int NodeIndex)> BeamSearchLayer0(
        HnswGraph graph,
        ReadOnlySpan<float> queryVector,
        int entryPoint,
        int ef,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn)
    {
        float entryDist = distanceFn(queryVector, resolver.GetVector(entryPoint));

        using var visited = new VisitedSet(graph.NodeCount);
        visited.Visit(entryPoint);

        // Candidates to explore: min-heap pops nearest first
        var candidates = new CandidateHeap(graph.NodeCount, isMinHeap: true);
        // Result set: max-heap keeps ef nearest; root = farthest (worst) for eviction
        var result = new CandidateHeap(ef + 1, isMinHeap: false);

        candidates.Push(entryDist, entryPoint);
        result.Push(entryDist, entryPoint);

        while (!candidates.IsEmpty)
        {
            var nearest = candidates.Pop();

            // If nearest candidate is farther than the farthest result, stop
            if (result.Count >= ef && nearest.Distance > result.PeekDistance())
                break;

            // Explore neighbors at layer 0
            var neighbors = graph.GetNeighbors(0, nearest.NodeIndex);
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
                        result.Pop(); // evict farthest
                }
            }
        }

        var output = result.DrainToList();
        candidates.Dispose();
        result.Dispose();
        return output;
    }
}
