// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Model;

/// <summary>
/// Configuration for the PageRank algorithm.
/// </summary>
public readonly record struct PageRankOptions
{
    /// <summary>Damping factor (probability of following an edge vs. teleporting). Default 0.85.</summary>
    public double DampingFactor { get; init; }

    /// <summary>Convergence threshold. Iteration stops when max score change is below this. Default 1e-6.</summary>
    public double Epsilon { get; init; }

    /// <summary>Maximum iterations. Default 100.</summary>
    public int MaxIterations { get; init; }

    /// <summary>Optional edge kind filter. If set, only edges of this kind are considered.</summary>
    public RelationKind? Kind { get; init; }

    /// <summary>Default constructor with standard values.</summary>
    public PageRankOptions()
    {
        DampingFactor = 0.85;
        Epsilon = 1e-6;
        MaxIterations = 100;
        Kind = null;
    }
}
