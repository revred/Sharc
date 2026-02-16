// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>
/// A parsed Sharq SELECT statement.
/// </summary>
internal sealed class SelectStatement
{
    public bool IsDistinct { get; init; }
    public required IReadOnlyList<SelectItem> Columns { get; init; }
    public required TableRef From { get; init; }
    public SharqStar? Where { get; init; }
    public IReadOnlyList<SharqStar>? GroupBy { get; init; }
    public SharqStar? Having { get; init; }
    public IReadOnlyList<OrderByItem>? OrderBy { get; init; }
    public SharqStar? Limit { get; init; }
    public SharqStar? Offset { get; init; }
    public IReadOnlyList<CoteDefinition>? Cotes { get; init; }
    public CompoundOp? CompoundOp { get; init; }
    public SelectStatement? CompoundRight { get; init; }
}

/// <summary>A Cote (Common Table Expression) definition: name AS (SELECT ...).</summary>
internal sealed class CoteDefinition
{
    public required string Name { get; init; }
    public required SelectStatement Query { get; init; }
}

/// <summary>Compound SELECT operator.</summary>
internal enum CompoundOp : byte
{
    Union,
    UnionAll,
    Intersect,
    Except
}
