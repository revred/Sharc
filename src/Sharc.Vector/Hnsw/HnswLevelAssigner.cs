// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Assigns random levels for each node using the HNSW layer distribution formula.
/// </summary>
internal static class HnswLevelAssigner
{
    internal static int[] AssignLevels(int nodeCount, Random rng, double mL, out int maxLevel)
    {
        var levels = new int[nodeCount];
        int localMax = 0;

        for (int i = 0; i < nodeCount; i++)
        {
            int level = RandomLevel(rng, mL);
            levels[i] = level;
            if (level > localMax)
                localMax = level;
        }

        maxLevel = localMax;
        return levels;
    }

    /// <summary>
    /// Assigns a single random level for one node.
    /// </summary>
    internal static int AssignSingleLevel(Random rng, double mL)
        => RandomLevel(rng, mL);

    /// <summary>
    /// Generates a random layer level using floor(-ln(uniform) * mL).
    /// </summary>
    private static int RandomLevel(Random rng, double mL)
        => (int)(-Math.Log(rng.NextDouble()) * mL);
}
