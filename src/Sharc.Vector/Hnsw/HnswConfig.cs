// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Configuration parameters for HNSW index construction and search.
/// </summary>
/// <remarks>
/// <para><see cref="M"/> controls the number of bidirectional connections per node per layer.
/// Higher values improve recall but increase memory and build time.</para>
/// <para><see cref="EfConstruction"/> is the beam width during index construction.
/// Higher values improve graph quality at the cost of slower build.</para>
/// <para><see cref="EfSearch"/> is the default beam width during search.
/// Can be overridden per-query for recall-latency trade-off.</para>
/// </remarks>
public readonly record struct HnswConfig
{
    /// <summary>Max bidirectional connections per node per layer (layers 1+). Default: 16.</summary>
    public int M { get; init; }

    /// <summary>Max connections at layer 0 (typically 2*M). Default: 32.</summary>
    public int M0 { get; init; }

    /// <summary>Beam width during index construction. Default: 200.</summary>
    public int EfConstruction { get; init; }

    /// <summary>Default beam width for search queries. Default: 50.</summary>
    public int EfSearch { get; init; }

    /// <summary>Use heuristic neighbor selection (Algorithm 4) for better recall. Default: true.</summary>
    public bool UseHeuristic { get; init; }

    /// <summary>Random seed for layer assignment. 0 = non-deterministic.</summary>
    public int Seed { get; init; }

    /// <summary>Level multiplier: 1 / ln(M). Controls expected number of layers.</summary>
    internal double ML => 1.0 / Math.Log(M);

    /// <summary>Default configuration: M=16, M0=32, efConstruction=200, efSearch=50, heuristic=true.</summary>
    public static HnswConfig Default => new()
    {
        M = 16,
        M0 = 32,
        EfConstruction = 200,
        EfSearch = 50,
        UseHeuristic = true,
        Seed = 0
    };

    /// <summary>Validates the configuration and throws if invalid.</summary>
    internal void Validate()
    {
        if (M < 2)
            throw new ArgumentOutOfRangeException(nameof(M), M, "M must be at least 2.");
        if (M0 < M)
            throw new ArgumentOutOfRangeException(nameof(M0), M0, "M0 must be at least M.");
        if (EfConstruction < 1)
            throw new ArgumentOutOfRangeException(nameof(EfConstruction), EfConstruction,
                "EfConstruction must be at least 1.");
        if (EfSearch < 1)
            throw new ArgumentOutOfRangeException(nameof(EfSearch), EfSearch,
                "EfSearch must be at least 1.");
    }
}
