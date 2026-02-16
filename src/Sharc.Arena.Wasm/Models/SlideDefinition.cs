// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// Defines a single benchmark slide with its base results and density configuration.
/// </summary>
public sealed class SlideDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Icon { get; init; }
    public required string Unit { get; init; }
    public required string CategoryId { get; init; }
    public required string Methodology { get; init; }
    public required IReadOnlyList<DensityTier> DensityTiers { get; init; }
    public required string DefaultDensity { get; init; }
    public required string ScaleMode { get; init; }

    /// <summary>Base results keyed by engine ID (at scale=1).</summary>
    public required IReadOnlyDictionary<string, EngineBaseResult> BaseResults { get; init; }

    /// <summary>Brief explanation of why this benchmark matters for AI agents.</summary>
    public string? WhyItMatters { get; init; }

    /// <summary>Section grouping (point-ops, scan-ops, graph-ops, trust-meta).</summary>
    public string? SectionId { get; init; }
}