// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>Null sort ordering for ORDER BY.</summary>
internal enum NullOrdering : byte
{
    NullsFirst,
    NullsLast
}

/// <summary>
/// An item in an ORDER BY clause: an expression with sort direction and null ordering.
/// </summary>
internal sealed class OrderByItem
{
    public required SharqStar Expression { get; init; }
    public bool Descending { get; init; }
    public NullOrdering? NullOrdering { get; init; }
}
