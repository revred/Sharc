// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq.Ast;

namespace Sharc.Query.Sharq;

/// <summary>
/// Zero-allocation recursive descent parser for Sharq queries.
/// Operates as a ref struct, consuming tokens from <see cref="SharqTokenizer"/>.
/// </summary>
internal ref struct SharqParser
{
    private SharqTokenizer _tokenizer;
    private SharqToken _current;
    private readonly ReadOnlySpan<char> _sql;

    private SharqParser(ReadOnlySpan<char> sql)
    {
        _sql = sql;
        _tokenizer = new SharqTokenizer(sql);
        _current = _tokenizer.NextToken();
    }

    /// <summary>
    /// Parses a complete SELECT statement from a Sharq string.
    /// </summary>
    public static SelectStatement Parse(string sql)
        => Parse(sql.AsSpan());

    /// <summary>
    /// Parses a complete SELECT statement from a Sharq span.
    /// </summary>
    public static SelectStatement Parse(ReadOnlySpan<char> sql)
    {
        var parser = new SharqParser(sql);
        return parser.ParseStatement();
    }

    /// <summary>
    /// Parses a standalone expression (for testing and filter compilation).
    /// </summary>
    public static SharqStar ParseExpression(string sql)
        => ParseExpression(sql.AsSpan());

    /// <summary>
    /// Parses a standalone expression from a span.
    /// </summary>
    public static SharqStar ParseExpression(ReadOnlySpan<char> sql)
    {
        var parser = new SharqParser(sql);
        return parser.ParseExpr();
    }

    // ─── Statement Parsing ──────────────────────────────────────────

    private SelectStatement ParseStatement()
    {
        // Optional WITH cote_list
        IReadOnlyList<CoteDefinition>? cotes = null;
        if (_current.Kind == SharqTokenKind.With)
        {
            Advance();
            cotes = ParseCoteList();
        }

        var stmt = ParseSelectCompound();

        // Optional trailing semicolon (moved from ParseSelect)
        Match(SharqTokenKind.Semicolon);

        if (cotes != null)
        {
            // Attach Cotes to the outermost statement
            return new SelectStatement
            {
                IsDistinct = stmt.IsDistinct,
                Columns = stmt.Columns,
                From = stmt.From,
                Where = stmt.Where,
                GroupBy = stmt.GroupBy,
                Having = stmt.Having,
                OrderBy = stmt.OrderBy,
                Limit = stmt.Limit,
                Offset = stmt.Offset,
                Joins = stmt.Joins,
                Cotes = cotes,
                CompoundOp = stmt.CompoundOp,
                CompoundRight = stmt.CompoundRight
            };
        }

        return stmt;
    }

    private List<CoteDefinition> ParseCoteList()
    {
        var cotes = new List<CoteDefinition>();
        do
        {
            string name = ExpectIdentifierText();
            Expect(SharqTokenKind.As);
            Expect(SharqTokenKind.LeftParen);
            var query = ParseSelect();
            Expect(SharqTokenKind.RightParen);
            cotes.Add(new CoteDefinition { Name = name, Query = query });
        } while (Match(SharqTokenKind.Comma));
        return cotes;
    }

    private SelectStatement ParseSelectCompound()
    {
        var left = ParseSelect();

        CompoundOp? compoundOp = _current.Kind switch
        {
            SharqTokenKind.Union => CompoundOp.Union,
            SharqTokenKind.UnionAll => CompoundOp.UnionAll,
            SharqTokenKind.Intersect => CompoundOp.Intersect,
            SharqTokenKind.Except => CompoundOp.Except,
            _ => null
        };

        if (compoundOp == null)
            return left;

        Advance(); // consume UNION/INTERSECT/EXCEPT

        // UNION ALL
        if (compoundOp == CompoundOp.Union)
        {
            var text = _current.Kind == SharqTokenKind.Identifier ? GetTokenIdentifierText() : null;
            if (text != null && text.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                compoundOp = CompoundOp.UnionAll;
                Advance();
            }
        }

        // Right-recurse for chaining: SELECT ... UNION SELECT ... UNION SELECT ...
        var right = ParseSelectCompound();

        return new SelectStatement
        {
            IsDistinct = left.IsDistinct,
            Columns = left.Columns,
            From = left.From,
            Where = left.Where,
            GroupBy = left.GroupBy,
            Having = left.Having,
            OrderBy = left.OrderBy,
            Limit = left.Limit,
            Offset = left.Offset,
            Joins = left.Joins,
            CompoundOp = compoundOp,
            CompoundRight = right
        };
    }

    private SelectStatement ParseSelect()
    {
        Expect(SharqTokenKind.Select);

        bool isDistinct = Match(SharqTokenKind.Distinct);

        var columns = ParseSelectList();

        Expect(SharqTokenKind.From);
        var from = ParseTableRef();

        var joins = ParseJoinList();


        SharqStar? where = null;
        if (Match(SharqTokenKind.Where))
            where = ParseExpr();

        IReadOnlyList<SharqStar>? groupBy = null;
        if (Match(SharqTokenKind.Group))
        {
            Expect(SharqTokenKind.By);
            groupBy = ParseExprList();
        }

        SharqStar? having = null;
        if (Match(SharqTokenKind.Having))
            having = ParseExpr();

        IReadOnlyList<OrderByItem>? orderBy = null;
        if (Match(SharqTokenKind.Order))
        {
            Expect(SharqTokenKind.By);
            orderBy = ParseOrderByList();
        }

        SharqStar? limit = null;
        SharqStar? offset = null;
        if (Match(SharqTokenKind.Limit))
        {
            limit = ParseExpr();
            if (Match(SharqTokenKind.Offset))
                offset = ParseExpr();
        }

        return new SelectStatement
        {
            IsDistinct = isDistinct,
            Columns = columns,
            From = from,
            Joins = joins,
            Where = where,
            GroupBy = groupBy,
            Having = having,
            OrderBy = orderBy,
            Limit = limit,
            Offset = offset
        };
    }

    private List<SelectItem> ParseSelectList()
    {
        if (_current.Kind == SharqTokenKind.Star)
        {
            Advance();
            return [new SelectItem { Expression = new WildcardStar() }];
        }

        var items = new List<SelectItem>();
        do
        {
            var expr = ParseExpr();
            string? alias = null;
            if (Match(SharqTokenKind.As))
                alias = ExpectIdentifierText();
            items.Add(new SelectItem { Expression = expr, Alias = alias });
        } while (Match(SharqTokenKind.Comma));

        return items;
    }

    private TableRef ParseTableRef()
    {
        string name = ExpectIdentifierText();

        // Check for record ID: table:id
        string? recordId = null;
        if (Match(SharqTokenKind.Colon))
            recordId = ExpectIdentifierText();

        string? alias = null;
        if (Match(SharqTokenKind.As))
            alias = ExpectIdentifierText();
        else if (_current.Kind == SharqTokenKind.Identifier)
            alias = ExpectIdentifierText();

        return new TableRef { Name = name, Alias = alias, RecordId = recordId };
    }

    private List<JoinClause> ParseJoinList()
    {
        var joins = new List<JoinClause>();
        while (true)
        {
            JoinKind? kind = null;
            if (Match(SharqTokenKind.Inner)) { Expect(SharqTokenKind.Join); kind = JoinKind.Inner; }
            else if (Match(SharqTokenKind.Left)) { MatchIdentifier("OUTER"); Expect(SharqTokenKind.Join); kind = JoinKind.Left; }
            else if (Match(SharqTokenKind.Right)) { MatchIdentifier("OUTER"); Expect(SharqTokenKind.Join); kind = JoinKind.Right; }
            else if (Match(SharqTokenKind.Cross)) { Expect(SharqTokenKind.Join); kind = JoinKind.Cross; }
            else if (Match(SharqTokenKind.Join)) { kind = JoinKind.Inner; }

            if (kind == null) break;

            var table = ParseTableRef();
            SharqStar? onExpr = null;

            if (kind != JoinKind.Cross)
            {
                Expect(SharqTokenKind.On);
                onExpr = ParseExpr();
            }

            joins.Add(new JoinClause { Kind = kind.Value, Table = table, OnCondition = onExpr });
        }
        return joins;
    }

    private bool MatchIdentifier(string text)
    {
        if (_current.Kind == SharqTokenKind.Identifier && 
            GetTokenIdentifierText().Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return true;
        }
        return false;
    }

    private List<SharqStar> ParseExprList()
    {
        var items = new List<SharqStar>();
        do
        {
            items.Add(ParseExpr());
        } while (Match(SharqTokenKind.Comma));
        return items;
    }

    private List<OrderByItem> ParseOrderByList()
    {
        var items = new List<OrderByItem>();
        do
        {
            var expr = ParseExpr();
            bool descending = false;
            if (Match(SharqTokenKind.Desc))
                descending = true;
            else
                Match(SharqTokenKind.Asc); // consume optional ASC

            NullOrdering? nullOrdering = null;
            if (Match(SharqTokenKind.Nulls))
            {
                var text = GetTokenIdentifierText();
                if (text.Equals("FIRST", StringComparison.OrdinalIgnoreCase))
                    nullOrdering = NullOrdering.NullsFirst;
                else if (text.Equals("LAST", StringComparison.OrdinalIgnoreCase))
                    nullOrdering = NullOrdering.NullsLast;
                else
                    throw new SharqParseException("Expected FIRST or LAST after NULLS", _current.Start);
                Advance();
            }

            items.Add(new OrderByItem { Expression = expr, Descending = descending, NullOrdering = nullOrdering });
        } while (Match(SharqTokenKind.Comma));
        return items;
    }

    // ─── Expression Parsing (precedence climbing) ────────────────────

    private SharqStar ParseExpr() => ParseOr();

    private SharqStar ParseOr()
    {
        var left = ParseAnd();
        while (_current.Kind == SharqTokenKind.Or)
        {
            Advance();
            var right = ParseAnd();
            left = new BinaryStar { Left = left, Op = BinaryOp.Or, Right = right };
        }
        return left;
    }

    private SharqStar ParseAnd()
    {
        var left = ParseNot();
        while (_current.Kind == SharqTokenKind.And)
        {
            Advance();
            var right = ParseNot();
            left = new BinaryStar { Left = left, Op = BinaryOp.And, Right = right };
        }
        return left;
    }

    private SharqStar ParseNot()
    {
        if (_current.Kind == SharqTokenKind.Not)
        {
            Advance();
            var operand = ParseNot();
            return new UnaryStar { Op = UnaryOp.Not, Operand = operand };
        }
        return ParseComparison();
    }

    private SharqStar ParseComparison()
    {
        var left = ParseAddition();

        // IS [NOT] NULL
        if (_current.Kind == SharqTokenKind.Is)
        {
            Advance();
            bool negated = Match(SharqTokenKind.Not);
            Expect(SharqTokenKind.Null);
            return new IsNullStar { Operand = left, Negated = negated };
        }

        // [NOT] BETWEEN / IN / LIKE
        if (_current.Kind == SharqTokenKind.Not)
        {
            // Peek ahead: NOT BETWEEN, NOT IN, NOT LIKE
            var peek = _tokenizer.Peek();
            if (peek.Kind == SharqTokenKind.Between)
            {
                Advance(); // consume NOT
                Advance(); // consume BETWEEN
                var low = ParseAddition();
                Expect(SharqTokenKind.And);
                var high = ParseAddition();
                return new BetweenStar { Operand = left, Low = low, High = high, Negated = true };
            }
            if (peek.Kind == SharqTokenKind.In)
            {
                Advance(); // consume NOT
                Advance(); // consume IN
                Expect(SharqTokenKind.LeftParen);
                if (_current.Kind == SharqTokenKind.Select)
                {
                    var query = ParseSelect();
                    Expect(SharqTokenKind.RightParen);
                    return new InSubqueryStar { Operand = left, Query = query, Negated = true };
                }
                var values = ParseExprListUntilParen();
                Expect(SharqTokenKind.RightParen);
                return new InStar { Operand = left, Values = values, Negated = true };
            }
            if (peek.Kind == SharqTokenKind.Like)
            {
                Advance(); // consume NOT
                Advance(); // consume LIKE
                var pattern = ParseAddition();
                return new LikeStar { Operand = left, Pattern = pattern, Negated = true };
            }
        }

        // BETWEEN ... AND ...
        if (_current.Kind == SharqTokenKind.Between)
        {
            Advance();
            var low = ParseAddition();
            Expect(SharqTokenKind.And);
            var high = ParseAddition();
            return new BetweenStar { Operand = left, Low = low, High = high };
        }

        // IN (...) or IN (SELECT ...)
        if (_current.Kind == SharqTokenKind.In)
        {
            Advance();
            Expect(SharqTokenKind.LeftParen);
            if (_current.Kind == SharqTokenKind.Select)
            {
                var query = ParseSelect();
                Expect(SharqTokenKind.RightParen);
                return new InSubqueryStar { Operand = left, Query = query };
            }
            var values = ParseExprListUntilParen();
            Expect(SharqTokenKind.RightParen);
            return new InStar { Operand = left, Values = values };
        }

        // LIKE pattern
        if (_current.Kind == SharqTokenKind.Like)
        {
            Advance();
            var pattern = ParseAddition();
            return new LikeStar { Operand = left, Pattern = pattern };
        }

        // Comparison operators
        BinaryOp? op = _current.Kind switch
        {
            SharqTokenKind.Equal => BinaryOp.Equal,
            SharqTokenKind.NotEqual => BinaryOp.NotEqual,
            SharqTokenKind.LessThan => BinaryOp.LessThan,
            SharqTokenKind.GreaterThan => BinaryOp.GreaterThan,
            SharqTokenKind.LessOrEqual => BinaryOp.LessOrEqual,
            SharqTokenKind.GreaterOrEqual => BinaryOp.GreaterOrEqual,
            SharqTokenKind.Match => BinaryOp.Match,
            SharqTokenKind.MatchAnd => BinaryOp.MatchAnd,
            SharqTokenKind.MatchOr => BinaryOp.MatchOr,
            _ => null
        };

        if (op.HasValue)
        {
            Advance();
            var right = ParseAddition();
            return new BinaryStar { Left = left, Op = op.Value, Right = right };
        }

        return left;
    }

    private List<SharqStar> ParseParenExprList()
    {
        Expect(SharqTokenKind.LeftParen);
        var items = new List<SharqStar>();
        if (_current.Kind != SharqTokenKind.RightParen)
        {
            do
            {
                items.Add(ParseExpr());
            } while (Match(SharqTokenKind.Comma));
        }
        Expect(SharqTokenKind.RightParen);
        return items;
    }

    private SharqStar ParseAddition()
    {
        var left = ParseMultiplication();
        while (_current.Kind is SharqTokenKind.Plus or SharqTokenKind.Minus)
        {
            var op = _current.Kind == SharqTokenKind.Plus ? BinaryOp.Add : BinaryOp.Subtract;
            Advance();
            var right = ParseMultiplication();
            left = new BinaryStar { Left = left, Op = op, Right = right };
        }
        return left;
    }

    private SharqStar ParseMultiplication()
    {
        var left = ParseUnary();
        while (_current.Kind is SharqTokenKind.Star or SharqTokenKind.Slash or SharqTokenKind.Percent)
        {
            var op = _current.Kind switch
            {
                SharqTokenKind.Star => BinaryOp.Multiply,
                SharqTokenKind.Slash => BinaryOp.Divide,
                _ => BinaryOp.Modulo
            };
            Advance();
            var right = ParseUnary();
            left = new BinaryStar { Left = left, Op = op, Right = right };
        }
        return left;
    }

    private SharqStar ParseUnary()
    {
        if (_current.Kind == SharqTokenKind.Minus)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryStar { Op = UnaryOp.Negate, Operand = operand };
        }
        return ParsePrimary();
    }

    private SharqStar ParsePrimary()
    {
        switch (_current.Kind)
        {
            case SharqTokenKind.Integer:
            {
                var token = _current;
                Advance();
                return new LiteralStar { Kind = LiteralKind.Integer, IntegerValue = token.IntegerValue };
            }

            case SharqTokenKind.Float:
            {
                var token = _current;
                Advance();
                return new LiteralStar { Kind = LiteralKind.Float, FloatValue = token.FloatValue };
            }

            case SharqTokenKind.String:
            {
                var text = GetTokenText(_current);
                Advance();
                return new LiteralStar { Kind = LiteralKind.String, StringValue = text };
            }

            case SharqTokenKind.Null:
                Advance();
                return new LiteralStar { Kind = LiteralKind.Null };

            case SharqTokenKind.True:
                Advance();
                return new LiteralStar { Kind = LiteralKind.Bool, BoolValue = true };

            case SharqTokenKind.False:
                Advance();
                return new LiteralStar { Kind = LiteralKind.Bool, BoolValue = false };

            case SharqTokenKind.LeftParen:
            {
                Advance();
                if (_current.Kind == SharqTokenKind.Select)
                {
                    var query = ParseSelect();
                    Expect(SharqTokenKind.RightParen);
                    return new SubqueryStar { Query = query };
                }
                var expr = ParseExpr();
                Expect(SharqTokenKind.RightParen);
                return expr;
            }

            case SharqTokenKind.Parameter:
            {
                string paramName = GetTokenText(_current);
                Advance();
                return new ParameterStar { Name = paramName };
            }

            case SharqTokenKind.Case:
                return ParseCaseExpr();

            case SharqTokenKind.Cast:
                return ParseCastExpr();

            case SharqTokenKind.Exists:
                return ParseExistsExpr();

            // Arrow expressions starting with |> or <| or <|>
            case SharqTokenKind.Edge:
            case SharqTokenKind.BackEdge:
            case SharqTokenKind.BidiEdge:
                return ParseArrowExpr(source: null);

            case SharqTokenKind.Identifier:
                return ParseIdentifierExpr();

            default:
                // Try to handle keywords that could be used as identifiers in some contexts
                if (IsKeywordToken(_current.Kind))
                    return ParseIdentifierExpr();

                throw new SharqParseException(
                    $"Unexpected token '{_current.Kind}'", _current.Start);
        }
    }

    private SharqStar ParseIdentifierExpr()
    {
        string name = GetTokenIdentifierText();
        Advance();

        // Check for record ID: name:id
        if (_current.Kind == SharqTokenKind.Colon)
        {
            Advance();
            string id = GetTokenIdentifierText();
            Advance();

            var recordId = new RecordIdStar { Table = name, Id = id };

            // Record ID followed by arrow? e.g., person:alice->knows->...
            if (_current.Kind is SharqTokenKind.Edge or SharqTokenKind.BackEdge or SharqTokenKind.BidiEdge)
                return ParseArrowExpr(source: recordId);

            return recordId;
        }

        // Check for function call: name(...)
        if (_current.Kind == SharqTokenKind.LeftParen)
        {
            Advance(); // consume (
            var func = ParseFunctionArgs(name);

            // Window function: func(...) OVER (...)
            if (_current.Kind == SharqTokenKind.Over)
                return ParseWindowSpec(func);

            return func;
        }

        // Check for qualified name: name.field
        if (_current.Kind == SharqTokenKind.Dot)
        {
            Advance();
            string field = GetTokenIdentifierText();
            Advance();
            var colRef = new ColumnRefStar { TableAlias = name, Name = field };

            // Qualified name followed by arrow?
            if (_current.Kind is SharqTokenKind.Edge or SharqTokenKind.BackEdge or SharqTokenKind.BidiEdge)
                return ParseArrowExpr(source: colRef);

            return colRef;
        }

        var col = new ColumnRefStar { Name = name };

        // Simple identifier followed by arrow?
        if (_current.Kind is SharqTokenKind.Edge or SharqTokenKind.BackEdge or SharqTokenKind.BidiEdge)
            return ParseArrowExpr(source: col);

        return col;
    }

    private FunctionCallStar ParseFunctionArgs(string name)
    {
        // count(*)
        if (_current.Kind == SharqTokenKind.Star)
        {
            Advance();
            Expect(SharqTokenKind.RightParen);
            return new FunctionCallStar { Name = name, Arguments = [], IsStarArg = true };
        }

        // count(DISTINCT x)
        bool isDistinct = Match(SharqTokenKind.Distinct);

        // Empty args: func()
        if (_current.Kind == SharqTokenKind.RightParen)
        {
            Advance();
            return new FunctionCallStar { Name = name, Arguments = [], IsDistinct = isDistinct };
        }

        var args = new List<SharqStar>();
        do
        {
            args.Add(ParseExpr());
        } while (Match(SharqTokenKind.Comma));

        Expect(SharqTokenKind.RightParen);
        return new FunctionCallStar { Name = name, Arguments = args, IsDistinct = isDistinct };
    }

    private ArrowStar ParseArrowExpr(SharqStar? source)
    {
        var steps = new List<ArrowStep>();

        while (_current.Kind is SharqTokenKind.Edge or SharqTokenKind.BackEdge or SharqTokenKind.BidiEdge)
        {
            var direction = _current.Kind switch
            {
                SharqTokenKind.Edge => ArrowDirection.Forward,
                SharqTokenKind.BackEdge => ArrowDirection.Backward,
                _ => ArrowDirection.Bidirectional
            };
            Advance(); // consume arrow

            string identifier = GetTokenIdentifierText();
            Advance();

            steps.Add(new ArrowStep { Direction = direction, Identifier = identifier });
        }

        // Optional trailing .field or .*
        string? finalField = null;
        bool finalWildcard = false;
        if (_current.Kind == SharqTokenKind.Dot)
        {
            Advance();
            if (_current.Kind == SharqTokenKind.Star)
            {
                finalWildcard = true;
                Advance();
            }
            else
            {
                finalField = GetTokenIdentifierText();
                Advance();
            }
        }

        return new ArrowStar
        {
            Source = source,
            Steps = steps,
            FinalField = finalField,
            FinalWildcard = finalWildcard
        };
    }

    // ─── CASE / CAST ──────────────────────────────────────────────────

    private CaseStar ParseCaseExpr()
    {
        Advance(); // consume CASE

        var whens = new List<CaseWhen>();
        while (_current.Kind == SharqTokenKind.When)
        {
            Advance(); // consume WHEN
            var condition = ParseExpr();
            Expect(SharqTokenKind.Then);
            var result = ParseExpr();
            whens.Add(new CaseWhen { Condition = condition, Result = result });
        }

        if (whens.Count == 0)
            throw new SharqParseException("CASE requires at least one WHEN clause", _current.Start);

        SharqStar? elseExpr = null;
        if (Match(SharqTokenKind.Else))
            elseExpr = ParseExpr();

        Expect(SharqTokenKind.End);

        return new CaseStar { Whens = whens, ElseExpr = elseExpr };
    }

    private CastStar ParseCastExpr()
    {
        Advance(); // consume CAST
        Expect(SharqTokenKind.LeftParen);

        var operand = ParseExpr();
        Expect(SharqTokenKind.As);

        string typeName = ExpectIdentifierText();
        Expect(SharqTokenKind.RightParen);

        return new CastStar { Operand = operand, TypeName = typeName };
    }

    private ExistsStar ParseExistsExpr()
    {
        Advance(); // consume EXISTS
        Expect(SharqTokenKind.LeftParen);
        var query = ParseSelect();
        Expect(SharqTokenKind.RightParen);
        return new ExistsStar { Query = query };
    }

    /// <summary>
    /// Parses a comma-separated expression list, assuming '(' has already been consumed.
    /// Does NOT consume the closing ')'.
    /// </summary>
    private List<SharqStar> ParseExprListUntilParen()
    {
        var items = new List<SharqStar>();
        if (_current.Kind != SharqTokenKind.RightParen)
        {
            do
            {
                items.Add(ParseExpr());
            } while (Match(SharqTokenKind.Comma));
        }
        return items;
    }

    // ─── Window Functions ──────────────────────────────────────────────

    private WindowStar ParseWindowSpec(FunctionCallStar func)
    {
        Advance(); // consume OVER
        Expect(SharqTokenKind.LeftParen);

        IReadOnlyList<SharqStar>? partitionBy = null;
        IReadOnlyList<OrderByItem>? orderBy = null;
        WindowFrame? frame = null;

        // PARTITION BY
        if (_current.Kind == SharqTokenKind.Partition)
        {
            Advance(); // consume PARTITION
            Expect(SharqTokenKind.By);
            partitionBy = ParseExprList();
        }

        // ORDER BY
        if (_current.Kind == SharqTokenKind.Order)
        {
            Advance(); // consume ORDER
            Expect(SharqTokenKind.By);
            orderBy = ParseOrderByList();
        }

        // Frame clause: ROWS or RANGE (contextual identifiers)
        if (_current.Kind == SharqTokenKind.Identifier)
        {
            var text = GetTokenText(_current);
            if (text.Equals("ROWS", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("RANGE", StringComparison.OrdinalIgnoreCase))
            {
                var frameKind = text.Equals("ROWS", StringComparison.OrdinalIgnoreCase)
                    ? WindowFrameKind.Rows
                    : WindowFrameKind.Range;
                Advance(); // consume ROWS/RANGE

                // BETWEEN start AND end  —or—  single bound
                if (_current.Kind == SharqTokenKind.Between)
                {
                    Advance(); // consume BETWEEN
                    var start = ParseFrameBound();
                    Expect(SharqTokenKind.And);
                    var end = ParseFrameBound();
                    frame = new WindowFrame { Kind = frameKind, Start = start, End = end };
                }
                else
                {
                    var start = ParseFrameBound();
                    frame = new WindowFrame { Kind = frameKind, Start = start };
                }
            }
        }

        Expect(SharqTokenKind.RightParen);

        return new WindowStar
        {
            Function = func,
            PartitionBy = partitionBy,
            OrderBy = orderBy,
            Frame = frame
        };
    }

    private FrameBound ParseFrameBound()
    {
        // UNBOUNDED PRECEDING / UNBOUNDED FOLLOWING
        if (_current.Kind == SharqTokenKind.Identifier)
        {
            var text = GetTokenText(_current);
            if (text.Equals("UNBOUNDED", StringComparison.OrdinalIgnoreCase))
            {
                Advance(); // consume UNBOUNDED
                var dirText = GetTokenIdentifierText();
                if (dirText.Equals("PRECEDING", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    return new FrameBound { Kind = FrameBoundKind.UnboundedPreceding };
                }
                if (dirText.Equals("FOLLOWING", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    return new FrameBound { Kind = FrameBoundKind.UnboundedFollowing };
                }
                throw new SharqParseException("Expected PRECEDING or FOLLOWING after UNBOUNDED", _current.Start);
            }

            // CURRENT ROW
            if (text.Equals("CURRENT", StringComparison.OrdinalIgnoreCase))
            {
                Advance(); // consume CURRENT
                var rowText = GetTokenIdentifierText();
                if (!rowText.Equals("ROW", StringComparison.OrdinalIgnoreCase))
                    throw new SharqParseException("Expected ROW after CURRENT", _current.Start);
                Advance();
                return new FrameBound { Kind = FrameBoundKind.CurrentRow };
            }
        }

        // expr PRECEDING / expr FOLLOWING
        var offset = ParsePrimary();
        if (_current.Kind == SharqTokenKind.Identifier)
        {
            var dirText = GetTokenText(_current);
            if (dirText.Equals("PRECEDING", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new FrameBound { Kind = FrameBoundKind.ExprPreceding, Offset = offset };
            }
            if (dirText.Equals("FOLLOWING", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return new FrameBound { Kind = FrameBoundKind.ExprFollowing, Offset = offset };
            }
        }

        throw new SharqParseException("Expected PRECEDING, FOLLOWING, or CURRENT ROW in frame bound", _current.Start);
    }

    // ─── Token Helpers ────────────────────────────────────────────────

    private void Advance()
    {
        _current = _tokenizer.NextToken();
    }

    private bool Match(SharqTokenKind kind)
    {
        if (_current.Kind == kind)
        {
            Advance();
            return true;
        }
        return false;
    }

    private void Expect(SharqTokenKind kind)
    {
        if (_current.Kind != kind)
            throw new SharqParseException(
                $"Expected '{kind}', got '{_current.Kind}'", _current.Start);
        Advance();
    }

    private string GetTokenText(SharqToken token)
    {
        return _sql.Slice(token.Start, token.Length).ToString();
    }

    /// <summary>
    /// Gets identifier text from the current token. Accepts both Identifier tokens
    /// and keyword tokens (keywords can be used as identifiers in some contexts).
    /// </summary>
    private string GetTokenIdentifierText()
    {
        if (_current.Kind == SharqTokenKind.Identifier || IsKeywordToken(_current.Kind))
            return GetTokenText(_current);

        throw new SharqParseException(
            $"Expected identifier, got '{_current.Kind}'", _current.Start);
    }

    private string ExpectIdentifierText()
    {
        string text = GetTokenIdentifierText();
        Advance();
        return text;
    }

    private static bool IsKeywordToken(SharqTokenKind kind) => kind switch
    {
        SharqTokenKind.Select or SharqTokenKind.From or SharqTokenKind.Where or
        SharqTokenKind.Group or SharqTokenKind.By or SharqTokenKind.Order or
        SharqTokenKind.Asc or SharqTokenKind.Desc or SharqTokenKind.Limit or
        SharqTokenKind.Offset or SharqTokenKind.And or SharqTokenKind.Or or
        SharqTokenKind.Not or SharqTokenKind.In or SharqTokenKind.Between or
        SharqTokenKind.Like or SharqTokenKind.Is or SharqTokenKind.Null or
        SharqTokenKind.True or SharqTokenKind.False or SharqTokenKind.As or
        SharqTokenKind.Distinct or SharqTokenKind.Case or SharqTokenKind.When or
        SharqTokenKind.Then or SharqTokenKind.Else or SharqTokenKind.End or
        SharqTokenKind.Having or SharqTokenKind.Cast or
        SharqTokenKind.With or SharqTokenKind.Over or SharqTokenKind.Partition or
        SharqTokenKind.Nulls or SharqTokenKind.Union or SharqTokenKind.Intersect or
        SharqTokenKind.Except or SharqTokenKind.Exists or
        SharqTokenKind.UnionAll => true,
        _ => false
    };
}
