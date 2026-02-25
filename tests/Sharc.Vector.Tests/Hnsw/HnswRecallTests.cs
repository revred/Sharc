// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswRecallTests
{
    [Fact]
    public void Recall10_10KVectors_AtLeast95Percent()
    {
        const int n = 10_000;
        const int dims = 32;
        const int k = 10;
        const int numQueries = 50;

        var vectors = GenerateRandomVectors(n, dims, seed: 42);
        var rowIds = new long[n];
        for (int i = 0; i < n; i++) rowIds[i] = i;

        var config = HnswConfig.Default with { Seed = 42, EfSearch = 100 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean, config);

        // Compute brute-force ground truth and HNSW results for each query
        var rng = new Random(123);
        int totalCorrect = 0;
        int totalExpected = numQueries * k;

        for (int q = 0; q < numQueries; q++)
        {
            // Random query vector
            var query = new float[dims];
            for (int d = 0; d < dims; d++)
                query[d] = (float)(rng.NextDouble() * 2.0 - 1.0);

            // Brute-force ground truth
            var bruteForce = BruteForceKnn(vectors, rowIds, query, k, DistanceMetric.Euclidean);
            var groundTruthSet = new HashSet<long>(bruteForce);

            // HNSW search
            var hnswResult = index.Search(query, k, ef: 100);

            // Count matches
            for (int i = 0; i < hnswResult.Count; i++)
            {
                if (groundTruthSet.Contains(hnswResult[i].RowId))
                    totalCorrect++;
            }
        }

        double recall = (double)totalCorrect / totalExpected;
        Assert.True(recall >= 0.95,
            $"Recall@{k} = {recall:P1} ({totalCorrect}/{totalExpected}) — expected >= 95%");
    }

    [Fact]
    public void Recall10_CosineMetric_AtLeast95Percent()
    {
        const int n = 5_000;
        const int dims = 32;
        const int k = 10;
        const int numQueries = 30;

        var vectors = GenerateNormalizedVectors(n, dims, seed: 789);
        var rowIds = new long[n];
        for (int i = 0; i < n; i++) rowIds[i] = i;

        var config = HnswConfig.Default with { Seed = 789, EfSearch = 100 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Cosine, config);

        var rng = new Random(456);
        int totalCorrect = 0;
        int totalExpected = numQueries * k;

        for (int q = 0; q < numQueries; q++)
        {
            var query = GenerateNormalizedVector(dims, rng);

            var bruteForce = BruteForceKnn(vectors, rowIds, query, k, DistanceMetric.Cosine);
            var groundTruthSet = new HashSet<long>(bruteForce);

            var hnswResult = index.Search(query, k, ef: 100);

            for (int i = 0; i < hnswResult.Count; i++)
            {
                if (groundTruthSet.Contains(hnswResult[i].RowId))
                    totalCorrect++;
            }
        }

        double recall = (double)totalCorrect / totalExpected;
        Assert.True(recall >= 0.95,
            $"Recall@{k} = {recall:P1} ({totalCorrect}/{totalExpected}) — expected >= 95%");
    }

    [Fact]
    public void Recall10_HigherDimensions_AtLeast90Percent()
    {
        const int n = 2_000;
        const int dims = 128;
        const int k = 10;
        const int numQueries = 20;

        var vectors = GenerateRandomVectors(n, dims, seed: 999);
        var rowIds = new long[n];
        for (int i = 0; i < n; i++) rowIds[i] = i;

        // Higher ef for higher dimensions
        var config = HnswConfig.Default with { Seed = 999, EfSearch = 150 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean, config);

        var rng = new Random(321);
        int totalCorrect = 0;
        int totalExpected = numQueries * k;

        for (int q = 0; q < numQueries; q++)
        {
            var query = new float[dims];
            for (int d = 0; d < dims; d++)
                query[d] = (float)(rng.NextDouble() * 2.0 - 1.0);

            var bruteForce = BruteForceKnn(vectors, rowIds, query, k, DistanceMetric.Euclidean);
            var groundTruthSet = new HashSet<long>(bruteForce);

            var hnswResult = index.Search(query, k, ef: 150);

            for (int i = 0; i < hnswResult.Count; i++)
            {
                if (groundTruthSet.Contains(hnswResult[i].RowId))
                    totalCorrect++;
            }
        }

        double recall = (double)totalCorrect / totalExpected;
        Assert.True(recall >= 0.90,
            $"Recall@{k} = {recall:P1} ({totalCorrect}/{totalExpected}) — expected >= 90%");
    }

    private static long[] BruteForceKnn(float[][] vectors, long[] rowIds,
        float[] query, int k, DistanceMetric metric)
    {
        var distanceFn = VectorDistanceFunctions.Resolve(metric);
        var distances = new (float Distance, long RowId)[vectors.Length];
        for (int i = 0; i < vectors.Length; i++)
            distances[i] = (distanceFn(query, vectors[i]), rowIds[i]);

        Array.Sort(distances, (a, b) => a.Distance.CompareTo(b.Distance));

        int count = Math.Min(k, distances.Length);
        var result = new long[count];
        for (int i = 0; i < count; i++)
            result[i] = distances[i].RowId;
        return result;
    }

    private static float[][] GenerateRandomVectors(int count, int dims, int seed)
    {
        var rng = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dims];
            for (int d = 0; d < dims; d++)
                vectors[i][d] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        return vectors;
    }

    private static float[][] GenerateNormalizedVectors(int count, int dims, int seed)
    {
        var rng = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
            vectors[i] = GenerateNormalizedVector(dims, rng);
        return vectors;
    }

    private static float[] GenerateNormalizedVector(int dims, Random rng)
    {
        var v = new float[dims];
        float norm = 0;
        for (int d = 0; d < dims; d++)
        {
            v[d] = (float)(rng.NextDouble() * 2.0 - 1.0);
            norm += v[d] * v[d];
        }
        norm = MathF.Sqrt(norm);
        for (int d = 0; d < dims; d++)
            v[d] /= norm;
        return v;
    }
}
