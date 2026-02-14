// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Marker interface for composable filter expressions.
/// Built using <see cref="FilterStar"/> and compiled into an evaluation tree at reader creation.
/// </summary>
public interface IFilterStar { }

// â”€â”€ Internal expression tree nodes (uncompiled form) â”€â”€

internal sealed class AndExpression : IFilterStar
{
    internal IFilterStar[] Children { get; }
    internal AndExpression(IFilterStar[] children) => Children = children;
}

internal sealed class OrExpression : IFilterStar
{
    internal IFilterStar[] Children { get; }
    internal OrExpression(IFilterStar[] children) => Children = children;
}

internal sealed class NotExpression : IFilterStar
{
    internal IFilterStar Inner { get; }
    internal NotExpression(IFilterStar inner) => Inner = inner;
}

internal sealed class PredicateExpression : IFilterStar
{
    internal string? ColumnName { get; }
    internal int? ColumnOrdinal { get; }
    internal FilterOp Operator { get; }
    internal TypedFilterValue Value { get; }

    internal PredicateExpression(string? columnName, int? columnOrdinal, FilterOp op, TypedFilterValue value)
    {
        ColumnName = columnName;
        ColumnOrdinal = columnOrdinal;
        Operator = op;
        Value = value;
    }
}