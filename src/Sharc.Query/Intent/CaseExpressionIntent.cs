// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq.Ast;

namespace Sharc.Query.Intent;

/// <summary>
/// Describes a CASE expression in the SELECT list.
/// Stores the original AST for post-materialization evaluation.
/// </summary>
internal readonly struct CaseExpressionIntent
{
    /// <summary>The CASE expression AST node.</summary>
    public CaseStar Expression { get; init; }

    /// <summary>Output column alias (user-provided or synthetic _case_N).</summary>
    public string Alias { get; init; }

    /// <summary>Index in the final output column list.</summary>
    public int OutputOrdinal { get; init; }

    /// <summary>Column names referenced by this CASE expression (for projection planning).</summary>
    public IReadOnlyList<string> SourceColumns { get; init; }
}
