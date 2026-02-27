// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Context;

/// <summary>
/// Calculates the downstream impact (blast radius) of modifying a file or symbol.
/// The default open-source implementation performs a single-hop dependency lookup.
/// Proprietary implementations may use multi-hop traversal weighted by test coverage
/// and runtime execution metrics.
/// </summary>
public interface IImpactAnalyzer
{
    /// <summary>
    /// Analyzes the downstream impact of modifying the target path or symbol.
    /// </summary>
    /// <param name="targetPath">File path or symbol identifier to analyze.</param>
    /// <param name="options">Analysis options (max depth, revision filter).</param>
    /// <returns>Impact report with risk scoring and test recommendations.</returns>
    ImpactReport CalculateImpact(string targetPath, ImpactOptions? options = null);
}

/// <summary>
/// The result of a blast-radius analysis.
/// </summary>
public sealed class ImpactReport
{
    /// <summary>Direct dependencies (1-hop).</summary>
    public required IReadOnlyList<ImpactedFile> DirectDependencies { get; init; }

    /// <summary>Transitive dependencies (multi-hop, weighted by coverage).</summary>
    public required IReadOnlyList<ImpactedFile> TransitiveDependencies { get; init; }

    /// <summary>Overall risk score (0.0 = safe, 1.0 = critical).</summary>
    public double RiskScore { get; init; }

    /// <summary>Suggested test depth (unit, integration, e2e).</summary>
    public required string SuggestedTestDepth { get; init; }
}

/// <summary>
/// A file impacted by a change, with risk metadata.
/// </summary>
public sealed class ImpactedFile
{
    /// <summary>File path relative to repository root.</summary>
    public required string Path { get; init; }

    /// <summary>Hop distance from the changed target.</summary>
    public int HopDistance { get; init; }

    /// <summary>Test coverage density (0.0-1.0).</summary>
    public double TestCoverage { get; init; }

    /// <summary>Average execution time in milliseconds (from runtime_metrics).</summary>
    public double? AvgExecutionMs { get; init; }
}

/// <summary>
/// Options for impact analysis.
/// </summary>
public sealed class ImpactOptions
{
    /// <summary>Maximum hop depth for transitive analysis. Default: 3.</summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>Only analyze changes since this git revision.</summary>
    public string? SinceRevision { get; init; }
}
