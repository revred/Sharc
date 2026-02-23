// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq.Ast;

namespace Sharc.Query.Intent;

/// <summary>
/// Compiles a parsed Sharq <see cref="SelectStatement"/> into a <see cref="QueryIntent"/>
/// or a <see cref="QueryPlan"/> (for compound and Cote queries).
/// </summary>
public static class IntentCompiler
{
    /// <summary>
    /// Parses and compiles a Sharq query string into a <see cref="QueryIntent"/>.
    /// Throws <see cref="NotSupportedException"/> for compound and Cote queries.
    /// Use <see cref="CompilePlan(string)"/> for full support.
    /// </summary>
    public static QueryIntent Compile(string sharq) =>
        Compile(Sharq.SharqParser.Parse(sharq));

    /// <summary>
    /// Parses and compiles a Sharq query string into a <see cref="QueryPlan"/>
    /// that supports simple, compound (UNION/INTERSECT/EXCEPT), and Cote queries.
    /// </summary>
    public static QueryPlan CompilePlan(string sharq) =>
        CompilePlan(Sharq.SharqParser.Parse(sharq));

    /// <summary>
    /// Compiles a <see cref="SelectStatement"/> AST into a <see cref="QueryPlan"/>.
    /// </summary>
    internal static QueryPlan CompilePlan(SelectStatement statement)
    {
        // Compile Cotes if present
        IReadOnlyList<CoteIntent>? cotes = null;
        if (statement.Cotes is { Count: > 0 })
            cotes = CompileCotes(statement.Cotes);

        // Compound query?
        if (statement.CompoundOp != null)
        {
            var compound = CompileCompound(statement);
            return new QueryPlan { Compound = compound, Cotes = cotes, Hint = statement.Hint };
        }

        // Simple query (strip Cotes — they're already compiled above)
        var intent = CompileSimple(statement);
        return new QueryPlan { Simple = intent, Cotes = cotes, Hint = statement.Hint };
    }

    /// <summary>
    /// Compiles a <see cref="SelectStatement"/> AST into a flat <see cref="QueryIntent"/>.
    /// </summary>
    internal static QueryIntent Compile(SelectStatement statement)
    {
        // Guard: unsupported query features that are parsed but cannot be executed
        if (statement.CompoundOp != null)
            throw new NotSupportedException(
                $"{statement.CompoundOp} queries are not yet supported.");

        if (statement.Cotes is { Count: > 0 })
            throw new NotSupportedException(
                "Common Table Expressions (WITH) are not yet supported.");

        // Columns + aggregates: detect aggregate functions in SELECT list
        IReadOnlyList<string>? columns = null;
        List<AggregateIntent>? aggregates = null;

        if (statement.Columns.Count > 0 && statement.Columns[0].Expression is not WildcardStar)
        {
            var names = new string[statement.Columns.Count];
            for (int i = 0; i < statement.Columns.Count; i++)
            {
                var expr = statement.Columns[i].Expression;
                var alias = statement.Columns[i].Alias;

                if (expr is FunctionCallStar func && IsAggregateFunction(func.Name))
                {
                    aggregates ??= [];
                    var agg = CompileAggregate(func, alias, i);
                    aggregates.Add(agg);
                    names[i] = agg.Alias;
                }
                else
                {
                    names[i] = alias ?? ExtractColumnName(expr);
                }
            }
            columns = names;
        }

        // Filter
        PredicateIntent? filter = null;
        if (statement.Where is not null)
            filter = CompileFilter(statement.Where);

        // GROUP BY
        IReadOnlyList<string>? groupBy = null;
        if (statement.GroupBy is { Count: > 0 })
        {
            var groups = new string[statement.GroupBy.Count];
            for (int i = 0; i < statement.GroupBy.Count; i++)
                groups[i] = ExtractColumnName(statement.GroupBy[i]);
            groupBy = groups;
        }

        // HAVING
        PredicateIntent? havingFilter = null;
        if (statement.Having is not null)
            havingFilter = CompileFilter(statement.Having);

        // Order by
        IReadOnlyList<OrderIntent>? orderBy = null;
        if (statement.OrderBy is { Count: > 0 })
        {
            var orders = new OrderIntent[statement.OrderBy.Count];
            for (int i = 0; i < statement.OrderBy.Count; i++)
            {
                var item = statement.OrderBy[i];
                orders[i] = new OrderIntent
                {
                    ColumnName = ExtractColumnName(item.Expression),
                    Descending = item.Descending,
                    NullsFirst = item.NullOrdering == NullOrdering.NullsFirst,
                };
            }
            orderBy = orders;
        }

        // Limit / Offset
        long? limit = ExtractLongOrNull(statement.Limit);
        long? offset = ExtractLongOrNull(statement.Offset);

        return new QueryIntent
        {
            TableName = statement.From.Name,
            TableAlias = statement.From.Alias,
            TableRecordId = statement.From.RecordId,
            Joins = statement.Joins != null ? CompileJoins(statement.Joins) : null,
            Columns = columns,
            Filter = filter,
            OrderBy = orderBy,
            Limit = limit,
            Offset = offset,
            IsDistinct = statement.IsDistinct,
            Aggregates = aggregates,
            GroupBy = groupBy,
            HavingFilter = havingFilter,
            Hint = statement.Hint,
        };
    }

    /// <summary>
    /// Compiles a WHERE expression into a flat <see cref="PredicateIntent"/>.
    /// </summary>
    internal static PredicateIntent CompileFilter(SharqStar expr)
    {
        var nodes = new List<PredicateNode>();
        int rootIndex = EmitNode(expr, nodes);
        return new PredicateIntent(nodes.ToArray(), rootIndex);
    }

    // ─── Post-order emission ────────────────────────────────────

    private static int EmitNode(SharqStar expr, List<PredicateNode> nodes)
    {
        return expr switch
        {
            BinaryStar binary => EmitBinary(binary, nodes),
            UnaryStar unary => EmitUnary(unary, nodes),
            IsNullStar isNull => EmitIsNull(isNull, nodes),
            BetweenStar between => EmitBetween(between, nodes),
            InStar inExpr => EmitIn(inExpr, nodes),
            LikeStar like => EmitLike(like, nodes),
            _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}"),
        };
    }

    private static int EmitBinary(BinaryStar binary, List<PredicateNode> nodes)
    {
        // Logical AND/OR: emit children first (post-order)
        if (binary.Op is BinaryOp.And or BinaryOp.Or)
        {
            int left = EmitNode(binary.Left, nodes);
            int right = EmitNode(binary.Right, nodes);
            int index = nodes.Count;
            nodes.Add(new PredicateNode
            {
                Op = binary.Op == BinaryOp.And ? IntentOp.And : IntentOp.Or,
                LeftIndex = left,
                RightIndex = right,
            });
            return index;
        }

        // Comparison: column op value
        string columnName = ExtractColumnName(binary.Left);
        IntentValue value = ExtractValue(binary.Right);
        IntentOp op = MapBinaryOp(binary.Op);

        int nodeIndex = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = op,
            ColumnName = columnName,
            Value = value,
        });
        return nodeIndex;
    }

    private static int EmitUnary(UnaryStar unary, List<PredicateNode> nodes)
    {
        int child = EmitNode(unary.Operand, nodes);
        int index = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = IntentOp.Not,
            LeftIndex = child,
        });
        return index;
    }

    private static int EmitIsNull(IsNullStar isNull, List<PredicateNode> nodes)
    {
        int index = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = isNull.Negated ? IntentOp.IsNotNull : IntentOp.IsNull,
            ColumnName = ExtractColumnName(isNull.Operand),
        });
        return index;
    }

    private static int EmitBetween(BetweenStar between, List<PredicateNode> nodes)
    {
        int index = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = IntentOp.Between,
            ColumnName = ExtractColumnName(between.Operand),
            Value = ExtractValue(between.Low),
            HighValue = ExtractValue(between.High),
        });
        return index;
    }

    private static int EmitIn(InStar inExpr, List<PredicateNode> nodes)
    {
        string columnName = ExtractColumnName(inExpr.Operand);
        IntentValue setVal = ExtractInSetValue(inExpr.Values);

        int index = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = inExpr.Negated ? IntentOp.NotIn : IntentOp.In,
            ColumnName = columnName,
            Value = setVal,
        });
        return index;
    }

    private static int EmitLike(LikeStar like, List<PredicateNode> nodes)
    {
        string columnName = ExtractColumnName(like.Operand);
        string pattern = ExtractStringLiteral(like.Pattern);

        // Decompose simple patterns into StartsWith/EndsWith/Contains
        IntentOp op;
        string value;

        if (pattern.Length >= 2 && pattern[0] == '%' && pattern[^1] == '%'
            && pattern.AsSpan(1, pattern.Length - 2).IndexOf('%') < 0)
        {
            op = IntentOp.Contains;
            value = pattern[1..^1];
        }
        else if (pattern.Length >= 1 && pattern[^1] == '%'
            && pattern.AsSpan(0, pattern.Length - 1).IndexOf('%') < 0)
        {
            op = IntentOp.StartsWith;
            value = pattern[..^1];
        }
        else if (pattern.Length >= 1 && pattern[0] == '%'
            && pattern.AsSpan(1).IndexOf('%') < 0)
        {
            op = IntentOp.EndsWith;
            value = pattern[1..];
        }
        else
        {
            op = like.Negated ? IntentOp.NotLike : IntentOp.Like;
            value = pattern;
        }

        int index = nodes.Count;
        nodes.Add(new PredicateNode
        {
            Op = op,
            ColumnName = columnName,
            Value = IntentValue.FromText(value),
        });
        return index;
    }

    // ─── Value extraction ───────────────────────────────────────

    private static IntentValue ExtractValue(SharqStar expr) => expr switch
    {
        LiteralStar lit => lit.Kind switch
        {
            LiteralKind.Integer => IntentValue.FromInt64(lit.IntegerValue),
            LiteralKind.Float => IntentValue.FromFloat64(lit.FloatValue),
            LiteralKind.String => IntentValue.FromText(lit.StringValue!),
            LiteralKind.Bool => IntentValue.FromBool(lit.BoolValue),
            LiteralKind.Null => IntentValue.Null,
            _ => IntentValue.Null,
        },
        ParameterStar p => IntentValue.FromParameter(p.Name),
        _ => throw new NotSupportedException($"Cannot extract value from {expr.GetType().Name}"),
    };

    private static IntentValue ExtractInSetValue(IReadOnlyList<SharqStar> values)
    {
        if (values.Count == 0)
            return IntentValue.FromInt64Set([]);

        // Determine set type from first element
        if (values[0] is LiteralStar { Kind: LiteralKind.Integer })
        {
            var set = new long[values.Count];
            for (int i = 0; i < values.Count; i++)
                set[i] = ((LiteralStar)values[i]).IntegerValue;
            return IntentValue.FromInt64Set(set);
        }

        if (values[0] is LiteralStar { Kind: LiteralKind.String })
        {
            var set = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                set[i] = ((LiteralStar)values[i]).StringValue!;
            return IntentValue.FromTextSet(set);
        }

        throw new NotSupportedException("IN values must be all integers or all strings");
    }

    private static string ExtractStringLiteral(SharqStar expr) =>
        expr is LiteralStar { Kind: LiteralKind.String, StringValue: not null } lit
            ? lit.StringValue
            : throw new NotSupportedException("LIKE pattern must be a string literal");

    private static string ExtractColumnName(SharqStar expr) => expr switch
    {
        ColumnRefStar col => FormatColumnRef(col),
        FunctionCallStar func when IsAggregateFunction(func.Name) => FormatAggregateName(func),
        _ => throw new NotSupportedException($"Expected column reference, got {expr.GetType().Name}"),
    };

    // ─── Aggregate helpers ───────────────────────────────────────

    private static readonly HashSet<string> s_aggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

    private static bool IsAggregateFunction(string name) =>
        s_aggregateFunctions.Contains(name);

    private static AggregateIntent CompileAggregate(FunctionCallStar func, string? alias, int outputOrdinal)
    {
        string upperName = func.Name.ToUpperInvariant();

        AggregateFunction aggFunc;
        string? columnName = null;

        if (func.IsStarArg && upperName == "COUNT")
        {
            aggFunc = AggregateFunction.CountStar;
        }
        else
        {
            columnName = func.Arguments.Count > 0
                ? ExtractColumnNameFromExpr(func.Arguments[0])
                : null;

            aggFunc = upperName switch
            {
                "COUNT" => AggregateFunction.Count,
                "SUM" => AggregateFunction.Sum,
                "AVG" => AggregateFunction.Avg,
                "MIN" => AggregateFunction.Min,
                "MAX" => AggregateFunction.Max,
                _ => throw new NotSupportedException($"Unsupported aggregate function: {func.Name}"),
            };
        }

        return new AggregateIntent
        {
            Function = aggFunc,
            ColumnName = columnName,
            Alias = alias ?? FormatAggregateName(func),
            OutputOrdinal = outputOrdinal,
        };
    }

    private static string FormatAggregateName(FunctionCallStar func) =>
        func.IsStarArg
            ? $"{func.Name.ToUpperInvariant()}(*)"
            : $"{func.Name.ToUpperInvariant()}({(func.Arguments.Count > 0 ? ExtractColumnNameFromExpr(func.Arguments[0]) : "")})";

    private static string ExtractColumnNameFromExpr(SharqStar expr) => expr switch
    {
        ColumnRefStar col => FormatColumnRef(col),
        _ => throw new NotSupportedException($"Expected column reference in aggregate, got {expr.GetType().Name}"),
    };

    private static long? ExtractLongOrNull(SharqStar? expr) => expr switch
    {
        null => null,
        LiteralStar { Kind: LiteralKind.Integer } lit => lit.IntegerValue,
        _ => null,
    };

    private static IntentOp MapBinaryOp(BinaryOp op) => op switch
    {
        BinaryOp.Equal => IntentOp.Eq,
        BinaryOp.NotEqual => IntentOp.Neq,
        BinaryOp.LessThan => IntentOp.Lt,
        BinaryOp.LessOrEqual => IntentOp.Lte,
        BinaryOp.GreaterThan => IntentOp.Gt,
        BinaryOp.GreaterOrEqual => IntentOp.Gte,
        _ => throw new NotSupportedException($"Unsupported binary operator for intent: {op}"),
    };

    // ─── Compound / Cote compilation ──────────────────────────────

    /// <summary>
    /// Compiles a simple (non-compound) <see cref="SelectStatement"/> into a <see cref="QueryIntent"/>.
    /// Same logic as <see cref="Compile(SelectStatement)"/> but without the compound/Cote guards.
    /// </summary>
    private static QueryIntent CompileSimple(SelectStatement statement)
    {
        // Columns + aggregates
        IReadOnlyList<string>? columns = null;
        List<AggregateIntent>? aggregates = null;

        if (statement.Columns.Count > 0 && statement.Columns[0].Expression is not WildcardStar)
        {
            var names = new string[statement.Columns.Count];
            for (int i = 0; i < statement.Columns.Count; i++)
            {
                var expr = statement.Columns[i].Expression;
                var alias = statement.Columns[i].Alias;

                if (expr is FunctionCallStar func && IsAggregateFunction(func.Name))
                {
                    aggregates ??= [];
                    var agg = CompileAggregate(func, alias, i);
                    aggregates.Add(agg);
                    names[i] = agg.Alias;
                }
                else
                {
                    names[i] = alias ?? ExtractColumnName(expr);
                }
            }
            columns = names;
        }

        PredicateIntent? filter = null;
        if (statement.Where is not null)
            filter = CompileFilter(statement.Where);

        IReadOnlyList<string>? groupBy = null;
        if (statement.GroupBy is { Count: > 0 })
        {
            var groups = new string[statement.GroupBy.Count];
            for (int i = 0; i < statement.GroupBy.Count; i++)
                groups[i] = ExtractColumnName(statement.GroupBy[i]);
            groupBy = groups;
        }

        PredicateIntent? havingFilter = null;
        if (statement.Having is not null)
            havingFilter = CompileFilter(statement.Having);

        IReadOnlyList<OrderIntent>? orderBy = null;
        if (statement.OrderBy is { Count: > 0 })
            orderBy = CompileOrderBy(statement.OrderBy);

        long? limit = ExtractLongOrNull(statement.Limit);
        long? offset = ExtractLongOrNull(statement.Offset);

        return new QueryIntent
        {
            TableName = statement.From.Name,
            TableAlias = statement.From.Alias,
            TableRecordId = statement.From.RecordId,
            Joins = statement.Joins != null ? CompileJoins(statement.Joins) : null,
            Columns = columns,
            Filter = filter,
            OrderBy = orderBy,
            Limit = limit,
            Offset = offset,
            IsDistinct = statement.IsDistinct,
            Aggregates = aggregates,
            GroupBy = groupBy,
            HavingFilter = havingFilter,
            Hint = statement.Hint,
        };
    }

    private static OrderIntent[] CompileOrderBy(IReadOnlyList<OrderByItem> items)
    {
        var orders = new OrderIntent[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            orders[i] = new OrderIntent
            {
                ColumnName = ExtractColumnName(items[i].Expression),
                Descending = items[i].Descending,
                NullsFirst = items[i].NullOrdering == NullOrdering.NullsFirst,
            };
        }
        return orders;
    }

    private static CoteIntent[] CompileCotes(IReadOnlyList<CoteDefinition> cotes)
    {
        var result = new CoteIntent[cotes.Count];
        for (int i = 0; i < cotes.Count; i++)
        {
            result[i] = new CoteIntent
            {
                Name = cotes[i].Name,
                Query = CompilePlan(cotes[i].Query),
            };
        }
        return result;
    }

    private static JoinIntent[] CompileJoins(IReadOnlyList<JoinClause> joins)
    {
        var result = new JoinIntent[joins.Count];
        for (int i = 0; i < joins.Count; i++)
        {
            var clause = joins[i];
            string? leftCol = null;
            string? rightCol = null;

            if (clause.Kind != JoinKind.Cross)
            {
                if (clause.OnCondition is BinaryStar { Op: BinaryOp.Equal } binary &&
                    binary.Left is ColumnRefStar l &&
                    binary.Right is ColumnRefStar r)
                {
                    // For now, naive extraction. The execution layer will validate aliases.
                    leftCol = FormatColumnRef(l);
                    rightCol = FormatColumnRef(r);
                }
                else
                {
                    throw new NotSupportedException($"JOIN ON must be an equality check between two columns (ON a.id = b.id). Got: {clause.OnCondition?.GetType().Name}");
                }
            }

            result[i] = new JoinIntent
            {
                Kind = MapJoinKind(clause.Kind),
                TableName = clause.Table.Name,
                TableAlias = clause.Table.Alias,
                LeftColumn = leftCol,
                RightColumn = rightCol
            };
        }
        return result;
    }

    private static JoinType MapJoinKind(JoinKind kind) => kind switch
    {
        JoinKind.Inner => JoinType.Inner,
        JoinKind.Left => JoinType.Left,
        JoinKind.Right => JoinType.Right,
        JoinKind.Cross => JoinType.Cross,
        _ => throw new NotSupportedException($"Unsupported join kind: {kind}")
    };

    private static string FormatColumnRef(ColumnRefStar col) =>
        string.IsNullOrEmpty(col.TableAlias) ? col.Name : $"{col.TableAlias}.{col.Name}";

    private static CompoundQueryPlan CompileCompound(SelectStatement statement)
    {
        // The compound statement has the LEFT query's fields + CompoundOp + CompoundRight
        var leftIntent = CompileSimple(statement);

        var op = MapCompoundOp(statement.CompoundOp!.Value);
        var right = statement.CompoundRight!;

        // Recurse: if the right side is itself a compound, chain
        if (right.CompoundOp != null)
        {
            var rightCompound = CompileCompound(right);

            // Bubble up FinalOrderBy/Limit/Offset from the inner compound to this level
            IReadOnlyList<OrderIntent>? finalOrderBy = rightCompound.FinalOrderBy;
            long? finalLimit = rightCompound.FinalLimit;
            long? finalOffset = rightCompound.FinalOffset;

            if (finalOrderBy != null || finalLimit != null || finalOffset != null)
            {
                // Clear from inner — they belong on the outermost compound
                rightCompound = new CompoundQueryPlan
                {
                    Left = rightCompound.Left,
                    Operator = rightCompound.Operator,
                    RightSimple = rightCompound.RightSimple,
                    RightCompound = rightCompound.RightCompound,
                };
            }

            return new CompoundQueryPlan
            {
                Left = leftIntent,
                Operator = op,
                RightCompound = rightCompound,
                FinalOrderBy = finalOrderBy,
                FinalLimit = finalLimit,
                FinalOffset = finalOffset,
            };
        }

        // Right side is a simple leaf — hoist ORDER BY/LIMIT to FinalOrderBy
        var rightIntent = CompileSimple(right);

        IReadOnlyList<OrderIntent>? hoistedOrderBy = rightIntent.OrderBy;
        long? hoistedLimit = rightIntent.Limit;
        long? hoistedOffset = rightIntent.Offset;

        // Strip them from the leaf so they don't apply to just the right sub-query
        if (hoistedOrderBy != null || hoistedLimit != null || hoistedOffset != null)
        {
            rightIntent = CloneWithoutFinalClauses(rightIntent);
        }

        return new CompoundQueryPlan
        {
            Left = leftIntent,
            Operator = op,
            RightSimple = rightIntent,
            FinalOrderBy = hoistedOrderBy,
            FinalLimit = hoistedLimit,
            FinalOffset = hoistedOffset,
        };
    }

    private static QueryIntent CloneWithoutFinalClauses(QueryIntent intent) => new()
    {
        TableName = intent.TableName,
        TableRecordId = intent.TableRecordId,
        Columns = intent.Columns,
        Filter = intent.Filter,
        OrderBy = null,
        Limit = null,
        Offset = null,
        IsDistinct = intent.IsDistinct,
        Aggregates = intent.Aggregates,
        GroupBy = intent.GroupBy,
        HavingFilter = intent.HavingFilter,
    };

    private static CompoundOperator MapCompoundOp(CompoundOp op) => op switch
    {
        Sharq.Ast.CompoundOp.Union => CompoundOperator.Union,
        Sharq.Ast.CompoundOp.UnionAll => CompoundOperator.UnionAll,
        Sharq.Ast.CompoundOp.Intersect => CompoundOperator.Intersect,
        Sharq.Ast.CompoundOp.Except => CompoundOperator.Except,
        _ => throw new NotSupportedException($"Unknown compound operator: {op}"),
    };
}
