// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>
/// A JOIN clause in a SELECT statement.
/// </summary>
internal sealed class JoinClause
{
    /// <summary>The type of join (Inner, Left, Cross).</summary>
    public required JoinKind Kind { get; init; }

    /// <summary>The table being joined.</summary>
    public required TableRef Table { get; init; }

    /// <summary>The ON condition (null for CROSS JOIN).</summary>
    public SharqStar? OnCondition { get; init; }
}

/// <summary>
/// The type of join operation.
/// </summary>
internal enum JoinKind : byte
{
    Inner,
    Left,
    Right,
    Cross,
    Full
}
