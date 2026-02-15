// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>Base class for all Sharq expression nodes.</summary>
internal abstract class SharqStar { }

/// <summary>Literal value: NULL, integer, float, string, or bool.</summary>
internal sealed class LiteralStar : SharqStar
{
    public LiteralKind Kind { get; init; }
    public long IntegerValue { get; init; }
    public double FloatValue { get; init; }
    public string? StringValue { get; init; }
    public bool BoolValue { get; init; }
}

/// <summary>The type of a literal value.</summary>
internal enum LiteralKind : byte
{
    Null,
    Integer,
    Float,
    String,
    Bool
}

/// <summary>Column reference: name or table.name.</summary>
internal sealed class ColumnRefStar : SharqStar
{
    public required string Name { get; init; }
    public string? TableAlias { get; init; }
}

/// <summary>Binary expression: left op right.</summary>
internal sealed class BinaryStar : SharqStar
{
    public required SharqStar Left { get; init; }
    public required BinaryOp Op { get; init; }
    public required SharqStar Right { get; init; }
}

/// <summary>Binary operators for expressions.</summary>
internal enum BinaryOp : byte
{
    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,

    // Comparison
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessOrEqual,
    GreaterOrEqual,

    // Logical
    And,
    Or,

    // Full-text match
    Match,      // @@
    MatchAnd,   // @AND@
    MatchOr,    // @OR@
}

/// <summary>Unary expression: NOT expr or -expr.</summary>
internal sealed class UnaryStar : SharqStar
{
    public required UnaryOp Op { get; init; }
    public required SharqStar Operand { get; init; }
}

/// <summary>Unary operators.</summary>
internal enum UnaryOp : byte
{
    Not,
    Negate
}

/// <summary>Function call: name([DISTINCT] args).</summary>
internal sealed class FunctionCallStar : SharqStar
{
    public required string Name { get; init; }
    public required IReadOnlyList<SharqStar> Arguments { get; init; }
    public bool IsDistinct { get; init; }
    public bool IsStarArg { get; init; }
}

/// <summary>IS [NOT] NULL expression.</summary>
internal sealed class IsNullStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public bool Negated { get; init; }
}

/// <summary>[NOT] BETWEEN low AND high.</summary>
internal sealed class BetweenStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public required SharqStar Low { get; init; }
    public required SharqStar High { get; init; }
    public bool Negated { get; init; }
}

/// <summary>[NOT] IN (value1, value2, ...).</summary>
internal sealed class InStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public required IReadOnlyList<SharqStar> Values { get; init; }
    public bool Negated { get; init; }
}

/// <summary>[NOT] LIKE pattern.</summary>
internal sealed class LikeStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public required SharqStar Pattern { get; init; }
    public bool Negated { get; init; }
}

/// <summary>SurrealDB-style graph arrow traversal.</summary>
internal sealed class ArrowStar : SharqStar
{
    public SharqStar? Source { get; init; }
    public required IReadOnlyList<ArrowStep> Steps { get; init; }
    public string? FinalField { get; init; }
    public bool FinalWildcard { get; init; }
}

/// <summary>A single step in an arrow traversal.</summary>
internal sealed class ArrowStep
{
    public required ArrowDirection Direction { get; init; }
    public required string Identifier { get; init; }
}

/// <summary>Arrow traversal direction.</summary>
internal enum ArrowDirection : byte
{
    Forward,    // ->
    Backward,   // <-
    Bidirectional // <->
}

/// <summary>Record ID literal: table:id (e.g. person:alice).</summary>
internal sealed class RecordIdStar : SharqStar
{
    public required string Table { get; init; }
    public required string Id { get; init; }
}

/// <summary>Wildcard star expression (SELECT *).</summary>
internal sealed class WildcardStar : SharqStar { }

/// <summary>CASE WHEN cond THEN result [WHEN...] [ELSE result] END.</summary>
internal sealed class CaseStar : SharqStar
{
    public required IReadOnlyList<CaseWhen> Whens { get; init; }
    public SharqStar? ElseExpr { get; init; }
}

/// <summary>A single WHEN condition / THEN result pair.</summary>
internal sealed class CaseWhen
{
    public required SharqStar Condition { get; init; }
    public required SharqStar Result { get; init; }
}

/// <summary>CAST(expression AS type_name).</summary>
internal sealed class CastStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public required string TypeName { get; init; }
}

/// <summary>Parameter reference: $name.</summary>
internal sealed class ParameterStar : SharqStar
{
    public required string Name { get; init; }
}

/// <summary>A subquery expression: (SELECT ...).</summary>
internal sealed class SubqueryStar : SharqStar
{
    public required SelectStatement Query { get; init; }
}

/// <summary>[NOT] IN (SELECT ...) — subquery variant of IN.</summary>
internal sealed class InSubqueryStar : SharqStar
{
    public required SharqStar Operand { get; init; }
    public required SelectStatement Query { get; init; }
    public bool Negated { get; init; }
}

/// <summary>EXISTS (SELECT ...) expression.</summary>
internal sealed class ExistsStar : SharqStar
{
    public required SelectStatement Query { get; init; }
}

/// <summary>Window function: func(...) OVER ([PARTITION BY ...] [ORDER BY ...] [frame]).</summary>
internal sealed class WindowStar : SharqStar
{
    public required FunctionCallStar Function { get; init; }
    public IReadOnlyList<SharqStar>? PartitionBy { get; init; }
    public IReadOnlyList<OrderByItem>? OrderBy { get; init; }
    public WindowFrame? Frame { get; init; }
}

/// <summary>Window frame specification: ROWS or RANGE with bounds.</summary>
internal sealed class WindowFrame
{
    public required WindowFrameKind Kind { get; init; }
    public required FrameBound Start { get; init; }
    public FrameBound? End { get; init; }
}

/// <summary>Window frame kind.</summary>
internal enum WindowFrameKind : byte { Rows, Range }

/// <summary>A single frame bound (start or end).</summary>
internal sealed class FrameBound
{
    public required FrameBoundKind Kind { get; init; }
    public SharqStar? Offset { get; init; }
}

/// <summary>Frame bound kind.</summary>
internal enum FrameBoundKind : byte
{
    UnboundedPreceding,
    UnboundedFollowing,
    CurrentRow,
    ExprPreceding,
    ExprFollowing
}
