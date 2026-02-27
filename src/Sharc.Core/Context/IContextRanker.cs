// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Context;

/// <summary>
/// Ranks context nodes by relevance to a natural-language query.
/// The default open-source implementation uses keyword matching.
/// Proprietary implementations may use multi-hop graph traversal,
/// runtime metrics, and LLM-specific token budgeting.
/// </summary>
public interface IContextRanker
{
    /// <summary>
    /// Returns context nodes ranked by relevance to the query,
    /// respecting the specified token budget.
    /// </summary>
    /// <param name="query">Natural-language query describing the task.</param>
    /// <param name="scope">Scoping parameters (budget, mode, path filter).</param>
    /// <returns>Ranked context nodes with confidence and provenance.</returns>
    IEnumerable<ContextNode> Rank(string query, ContextScope scope);
}

/// <summary>
/// A ranked context fragment with provenance and confidence.
/// </summary>
public sealed class ContextNode
{
    /// <summary>Kind of context: file, symbol, test, doc, edge, decision.</summary>
    public required string Kind { get; init; }

    /// <summary>File path relative to repository root.</summary>
    public required string Path { get; init; }

    /// <summary>Start line (1-based).</summary>
    public int LineStart { get; init; }

    /// <summary>End line (1-based).</summary>
    public int LineEnd { get; init; }

    /// <summary>Content excerpt or full source.</summary>
    public required string Content { get; init; }

    /// <summary>Confidence score (0.0 = no confidence, 1.0 = exact match).</summary>
    public double Confidence { get; init; }

    /// <summary>Estimated token count for this fragment.</summary>
    public int TokenEstimate { get; init; }
}

/// <summary>
/// Scoping parameters for a context query.
/// </summary>
public sealed class ContextScope
{
    /// <summary>Maximum token budget for the result set.</summary>
    public int TokenBudget { get; init; } = 8000;

    /// <summary>Mode: summary, source, or full.</summary>
    public string Mode { get; init; } = "source";

    /// <summary>Optional file path constraint.</summary>
    public string? PathFilter { get; init; }
}
