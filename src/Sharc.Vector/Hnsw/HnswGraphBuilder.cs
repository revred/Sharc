// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Builds an HNSW graph from a set of vectors. Implements Algorithm 1 from the HNSW paper.
/// </summary>
internal static class HnswGraphBuilder
{
    /// <summary>
    /// Builds a complete HNSW graph from the given vectors and row IDs.
    /// </summary>
    internal static HnswGraph Build(
        IVectorResolver resolver,
        long[] rowIds,
        DistanceMetric metric,
        HnswConfig config)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(rowIds);

        config.Validate();
        int nodeCount = rowIds.Length;
        if (nodeCount == 0)
            throw new InvalidOperationException("Cannot build HNSW index from zero vectors.");
        if (resolver.Dimensions <= 0)
            throw new InvalidOperationException("Vector dimensions must be greater than zero.");

        var rawDistanceFn = VectorDistanceFunctions.Resolve(metric);

        // DotProduct is a similarity score (higher is better). Normalize to distance semantics.
        bool isDotProduct = metric == DistanceMetric.DotProduct;
        VectorDistanceFunction distanceFn = isDotProduct
            ? (a, b) => -rawDistanceFn(a, b)
            : rawDistanceFn;

        var rng = config.Seed != 0 ? new Random(config.Seed) : new Random();
        int[] assignedLevels = HnswLevelAssigner.AssignLevels(
            nodeCount, rng, config.ML, out int globalMaxLevel);

        var graph = new HnswGraph(nodeCount, globalMaxLevel);

        // Insert first node.
        graph.SetRowId(0, rowIds[0]);
        graph.SetLevel(0, assignedLevels[0]);
        graph.EntryPoint = 0;
        graph.MaxLevel = assignedLevels[0];

        // Insert remaining nodes.
        for (int i = 1; i < nodeCount; i++)
        {
            graph.SetRowId(i, rowIds[i]);
            graph.SetLevel(i, assignedLevels[i]);
            InsertNode(graph, i, resolver, distanceFn, config);
        }

        return graph;
    }

    /// <summary>
    /// Inserts a single node into the graph (Algorithm 1).
    /// </summary>
    private static void InsertNode(
        HnswGraph graph,
        int newNode,
        IVectorResolver resolver,
        VectorDistanceFunction distanceFn,
        HnswConfig config)
    {
        var queryVector = resolver.GetVector(newNode);
        int nodeLevel = graph.GetLevel(newNode);
        int entryPoint = graph.EntryPoint;
        int currentMaxLevel = graph.MaxLevel;

        // Phase 1: greedy descent from entry point for layers above nodeLevel.
        if (currentMaxLevel > nodeLevel)
        {
            int closest = entryPoint;
            float closestDist = distanceFn(queryVector, resolver.GetVector(closest));

            for (int layer = currentMaxLevel; layer > nodeLevel; layer--)
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    var neighbors = graph.GetNeighbors(layer, closest);
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int neighbor = neighbors[i];
                        float dist = distanceFn(queryVector, resolver.GetVector(neighbor));
                        if (dist < closestDist)
                        {
                            closest = neighbor;
                            closestDist = dist;
                            changed = true;
                        }
                    }
                }
            }

            entryPoint = closest;
        }

        // Phase 2: insert from min(nodeLevel, currentMaxLevel) down to 0.
        int startLayer = Math.Min(nodeLevel, currentMaxLevel);
        for (int layer = startLayer; layer >= 0; layer--)
        {
            int maxConn = layer == 0 ? config.M0 : config.M;

            var candidatesList = HnswLayerSearcher.SearchLayer(
                graph, queryVector, entryPoint, config.EfConstruction, layer, resolver, distanceFn);
            var candidatesSpan = CollectionsMarshal.AsSpan(candidatesList);

            int[] selectedNeighbors = config.UseHeuristic
                ? HnswNeighborSelector.SelectHeuristic(
                    queryVector, candidatesSpan, maxConn, resolver, distanceFn)
                : HnswNeighborSelector.SelectSimple(candidatesSpan, maxConn);

            // newNode -> selectedNeighbors
            graph.SetNeighbors(layer, newNode, selectedNeighbors);

            // selectedNeighbor -> newNode (reverse edges with pruning)
            for (int i = 0; i < selectedNeighbors.Length; i++)
            {
                int neighbor = selectedNeighbors[i];
                HnswConnectionManager.AddConnection(
                    graph, layer, neighbor, newNode, maxConn, resolver, distanceFn, config);
            }

            // Nearest candidate becomes entry point for the next lower layer.
            if (candidatesList.Count > 0)
                entryPoint = candidatesList[0].NodeIndex;
        }

        if (nodeLevel > currentMaxLevel)
        {
            graph.EntryPoint = newNode;
            graph.MaxLevel = nodeLevel;
        }
    }
}
