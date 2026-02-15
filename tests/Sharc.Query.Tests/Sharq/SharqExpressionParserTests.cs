// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

public class SharqExpressionParserTests
{
    private static SharqStar ParseExpr(string sql)
        => SharqParser.ParseExpression(sql);

    [Fact]
    public void Parse_IntegerLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("42");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.Integer, lit.Kind);
        Assert.Equal(42L, lit.IntegerValue);
    }

    [Fact]
    public void Parse_FloatLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("3.14");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.Float, lit.Kind);
        Assert.Equal(3.14, lit.FloatValue, 5);
    }

    [Fact]
    public void Parse_StringLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("'hello'");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("hello", lit.StringValue);
    }

    [Fact]
    public void Parse_NullLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("NULL");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.Null, lit.Kind);
    }

    [Fact]
    public void Parse_TrueLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("TRUE");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
        Assert.True(lit.BoolValue);
    }

    [Fact]
    public void Parse_FalseLiteral_ReturnsLiteralStar()
    {
        var expr = ParseExpr("FALSE");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
        Assert.False(lit.BoolValue);
    }

    [Fact]
    public void Parse_SimpleIdentifier_ReturnsColumnRef()
    {
        var expr = ParseExpr("age");
        var col = Assert.IsType<ColumnRefStar>(expr);
        Assert.Equal("age", col.Name);
        Assert.Null(col.TableAlias);
    }

    [Fact]
    public void Parse_QualifiedIdentifier_ReturnsColumnRefWithAlias()
    {
        var expr = ParseExpr("u.name");
        var col = Assert.IsType<ColumnRefStar>(expr);
        Assert.Equal("name", col.Name);
        Assert.Equal("u", col.TableAlias);
    }

    [Fact]
    public void Parse_EqualComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x = 1");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Equal, bin.Op);
        Assert.IsType<ColumnRefStar>(bin.Left);
        Assert.IsType<LiteralStar>(bin.Right);
    }

    [Fact]
    public void Parse_NotEqualComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x != 1");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.NotEqual, bin.Op);
    }

    [Fact]
    public void Parse_LessThanComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x < 10");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.LessThan, bin.Op);
    }

    [Fact]
    public void Parse_GreaterThanComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x > 10");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.GreaterThan, bin.Op);
    }

    [Fact]
    public void Parse_LessOrEqualComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x <= 10");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.LessOrEqual, bin.Op);
    }

    [Fact]
    public void Parse_GreaterOrEqualComparison_ReturnsBinaryStar()
    {
        var expr = ParseExpr("x >= 10");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.GreaterOrEqual, bin.Op);
    }

    [Fact]
    public void Parse_Arithmetic_RespectsOperatorPrecedence()
    {
        // 1 + 2 * 3 should parse as 1 + (2 * 3)
        var expr = ParseExpr("1 + 2 * 3");
        var add = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Add, add.Op);
        var mul = Assert.IsType<BinaryStar>(add.Right);
        Assert.Equal(BinaryOp.Multiply, mul.Op);
    }

    [Fact]
    public void Parse_Parenthesized_OverridesPrecedence()
    {
        // (1 + 2) * 3 should parse as (1 + 2) * 3
        var expr = ParseExpr("(1 + 2) * 3");
        var mul = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Multiply, mul.Op);
        var add = Assert.IsType<BinaryStar>(mul.Left);
        Assert.Equal(BinaryOp.Add, add.Op);
    }

    [Fact]
    public void Parse_AndOr_RespectsLogicalPrecedence()
    {
        // a OR b AND c should parse as a OR (b AND c)
        var expr = ParseExpr("a OR b AND c");
        var or = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Or, or.Op);
        var and = Assert.IsType<BinaryStar>(or.Right);
        Assert.Equal(BinaryOp.And, and.Op);
    }

    [Fact]
    public void Parse_Not_ReturnsUnaryStar()
    {
        var expr = ParseExpr("NOT active");
        var un = Assert.IsType<UnaryStar>(expr);
        Assert.Equal(UnaryOp.Not, un.Op);
        Assert.IsType<ColumnRefStar>(un.Operand);
    }

    [Fact]
    public void Parse_IsNull_ReturnsIsNullStar()
    {
        var expr = ParseExpr("x IS NULL");
        var isNull = Assert.IsType<IsNullStar>(expr);
        Assert.False(isNull.Negated);
        Assert.IsType<ColumnRefStar>(isNull.Operand);
    }

    [Fact]
    public void Parse_IsNotNull_ReturnsIsNullStarNegated()
    {
        var expr = ParseExpr("x IS NOT NULL");
        var isNull = Assert.IsType<IsNullStar>(expr);
        Assert.True(isNull.Negated);
    }

    [Fact]
    public void Parse_Between_ReturnsBetweenStar()
    {
        var expr = ParseExpr("x BETWEEN 1 AND 10");
        var bet = Assert.IsType<BetweenStar>(expr);
        Assert.False(bet.Negated);
        Assert.IsType<ColumnRefStar>(bet.Operand);
        var low = Assert.IsType<LiteralStar>(bet.Low);
        Assert.Equal(1L, low.IntegerValue);
        var high = Assert.IsType<LiteralStar>(bet.High);
        Assert.Equal(10L, high.IntegerValue);
    }

    [Fact]
    public void Parse_NotBetween_ReturnsBetweenStarNegated()
    {
        var expr = ParseExpr("x NOT BETWEEN 1 AND 10");
        var bet = Assert.IsType<BetweenStar>(expr);
        Assert.True(bet.Negated);
    }

    [Fact]
    public void Parse_InList_ReturnsInStar()
    {
        var expr = ParseExpr("x IN (1, 2, 3)");
        var inExpr = Assert.IsType<InStar>(expr);
        Assert.False(inExpr.Negated);
        Assert.Equal(3, inExpr.Values.Count);
    }

    [Fact]
    public void Parse_NotIn_ReturnsInStarNegated()
    {
        var expr = ParseExpr("x NOT IN (1, 2)");
        var inExpr = Assert.IsType<InStar>(expr);
        Assert.True(inExpr.Negated);
    }

    [Fact]
    public void Parse_Like_ReturnsLikeStar()
    {
        var expr = ParseExpr("name LIKE '%test%'");
        var like = Assert.IsType<LikeStar>(expr);
        Assert.False(like.Negated);
        Assert.IsType<ColumnRefStar>(like.Operand);
        Assert.IsType<LiteralStar>(like.Pattern);
    }

    [Fact]
    public void Parse_NotLike_ReturnsLikeStarNegated()
    {
        var expr = ParseExpr("name NOT LIKE '%test%'");
        var like = Assert.IsType<LikeStar>(expr);
        Assert.True(like.Negated);
    }

    [Fact]
    public void Parse_MatchOperator_ReturnsBinaryStar()
    {
        var expr = ParseExpr("body @@ 'search terms'");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Match, bin.Op);
    }

    [Fact]
    public void Parse_MatchAndOperator_ReturnsBinaryStar()
    {
        var expr = ParseExpr("body @AND@ 'personal rare'");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.MatchAnd, bin.Op);
    }

    [Fact]
    public void Parse_MatchOrOperator_ReturnsBinaryStar()
    {
        var expr = ParseExpr("body @OR@ 'personal nice'");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.MatchOr, bin.Op);
    }

    [Fact]
    public void Parse_FunctionCall_Count_ReturnsFunctionCallStar()
    {
        var expr = ParseExpr("count(*)");
        var fn = Assert.IsType<FunctionCallStar>(expr);
        Assert.Equal("count", fn.Name);
        Assert.True(fn.IsStarArg);
    }

    [Fact]
    public void Parse_FunctionCall_Sum_ReturnsFunctionCallStar()
    {
        var expr = ParseExpr("sum(amount)");
        var fn = Assert.IsType<FunctionCallStar>(expr);
        Assert.Equal("sum", fn.Name);
        Assert.Single(fn.Arguments);
        Assert.IsType<ColumnRefStar>(fn.Arguments[0]);
    }

    [Fact]
    public void Parse_FunctionCall_Avg_ReturnsFunctionCallStar()
    {
        var expr = ParseExpr("avg(score)");
        var fn = Assert.IsType<FunctionCallStar>(expr);
        Assert.Equal("avg", fn.Name);
    }

    [Fact]
    public void Parse_FunctionCallDistinct_SetsDistinctFlag()
    {
        var expr = ParseExpr("count(DISTINCT category)");
        var fn = Assert.IsType<FunctionCallStar>(expr);
        Assert.Equal("count", fn.Name);
        Assert.True(fn.IsDistinct);
        Assert.Single(fn.Arguments);
    }

    [Fact]
    public void Parse_UnaryMinus_ReturnsUnaryStar()
    {
        var expr = ParseExpr("-42");
        var un = Assert.IsType<UnaryStar>(expr);
        Assert.Equal(UnaryOp.Negate, un.Op);
        var lit = Assert.IsType<LiteralStar>(un.Operand);
        Assert.Equal(42L, lit.IntegerValue);
    }

    [Fact]
    public void Parse_RecordId_ReturnsRecordIdStar()
    {
        var expr = ParseExpr("person:alice");
        var rid = Assert.IsType<RecordIdStar>(expr);
        Assert.Equal("person", rid.Table);
        Assert.Equal("alice", rid.Id);
    }

    [Fact]
    public void Parse_NestedExpressions_BuildsCorrectTree()
    {
        // a > 1 AND (b < 2 OR c = 3)
        var expr = ParseExpr("a > 1 AND (b < 2 OR c = 3)");
        var and = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.And, and.Op);

        var left = Assert.IsType<BinaryStar>(and.Left);
        Assert.Equal(BinaryOp.GreaterThan, left.Op);

        var right = Assert.IsType<BinaryStar>(and.Right);
        Assert.Equal(BinaryOp.Or, right.Op);
    }

    [Fact]
    public void Parse_MultipleArithmetic_LeftAssociative()
    {
        // 1 - 2 - 3 should parse as (1 - 2) - 3
        var expr = ParseExpr("1 - 2 - 3");
        var outer = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Subtract, outer.Op);
        var inner = Assert.IsType<BinaryStar>(outer.Left);
        Assert.Equal(BinaryOp.Subtract, inner.Op);
    }

    // ─── CASE WHEN/THEN/ELSE/END ────────────────────────────────────

    [Fact]
    public void Parse_CaseWhenThenEnd_ReturnsCaseStar()
    {
        var expr = ParseExpr("CASE WHEN x = 1 THEN 'one' END");
        var caseExpr = Assert.IsType<CaseStar>(expr);
        Assert.Single(caseExpr.Whens);
        Assert.Null(caseExpr.ElseExpr);

        var when = caseExpr.Whens[0];
        var cond = Assert.IsType<BinaryStar>(when.Condition);
        Assert.Equal(BinaryOp.Equal, cond.Op);
        var result = Assert.IsType<LiteralStar>(when.Result);
        Assert.Equal("one", result.StringValue);
    }

    [Fact]
    public void Parse_CaseWhenThenElseEnd_ReturnsElseExpr()
    {
        var expr = ParseExpr("CASE WHEN x = 1 THEN 'one' ELSE 'other' END");
        var caseExpr = Assert.IsType<CaseStar>(expr);
        Assert.Single(caseExpr.Whens);
        Assert.NotNull(caseExpr.ElseExpr);
        var elseVal = Assert.IsType<LiteralStar>(caseExpr.ElseExpr);
        Assert.Equal("other", elseVal.StringValue);
    }

    [Fact]
    public void Parse_CaseMultipleWhens_ReturnsAllBranches()
    {
        var expr = ParseExpr(
            "CASE WHEN x = 1 THEN 'one' WHEN x = 2 THEN 'two' WHEN x = 3 THEN 'three' ELSE 'many' END");
        var caseExpr = Assert.IsType<CaseStar>(expr);
        Assert.Equal(3, caseExpr.Whens.Count);
        Assert.NotNull(caseExpr.ElseExpr);
    }

    [Fact]
    public void Parse_CaseNestedExpressions_WorksCorrectly()
    {
        var expr = ParseExpr("CASE WHEN a > 0 AND b < 10 THEN a + b ELSE 0 END");
        var caseExpr = Assert.IsType<CaseStar>(expr);
        Assert.Single(caseExpr.Whens);
        var cond = Assert.IsType<BinaryStar>(caseExpr.Whens[0].Condition);
        Assert.Equal(BinaryOp.And, cond.Op);
        var result = Assert.IsType<BinaryStar>(caseExpr.Whens[0].Result);
        Assert.Equal(BinaryOp.Add, result.Op);
    }

    [Fact]
    public void Parse_CaseCaseInsensitive_Works()
    {
        var expr = ParseExpr("case when x = 1 then 'a' else 'b' end");
        var caseExpr = Assert.IsType<CaseStar>(expr);
        Assert.Single(caseExpr.Whens);
        Assert.NotNull(caseExpr.ElseExpr);
    }

    // ─── CAST ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CastAsInteger_ReturnsCastStar()
    {
        var expr = ParseExpr("CAST(x AS INTEGER)");
        var cast = Assert.IsType<CastStar>(expr);
        Assert.Equal("INTEGER", cast.TypeName);
        var col = Assert.IsType<ColumnRefStar>(cast.Operand);
        Assert.Equal("x", col.Name);
    }

    [Fact]
    public void Parse_CastAsText_ReturnsCastStar()
    {
        var expr = ParseExpr("CAST(42 AS TEXT)");
        var cast = Assert.IsType<CastStar>(expr);
        Assert.Equal("TEXT", cast.TypeName);
        var lit = Assert.IsType<LiteralStar>(cast.Operand);
        Assert.Equal(42L, lit.IntegerValue);
    }

    [Fact]
    public void Parse_CastNested_WorksCorrectly()
    {
        var expr = ParseExpr("CAST(a + b AS REAL)");
        var cast = Assert.IsType<CastStar>(expr);
        Assert.Equal("REAL", cast.TypeName);
        var bin = Assert.IsType<BinaryStar>(cast.Operand);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }

    [Fact]
    public void Parse_CastCaseInsensitive_Works()
    {
        var expr = ParseExpr("cast(x as integer)");
        var cast = Assert.IsType<CastStar>(expr);
        Assert.Equal("integer", cast.TypeName);
    }

    // ─── String concat via + ───────────────────────────────────────────

    [Fact]
    public void Parse_StringConcatWithPlus_ReturnsBinaryStar()
    {
        // + handles both arithmetic and string concat (runtime resolves by type)
        var expr = ParseExpr("first_name + last_name");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Add, bin.Op);
        var left = Assert.IsType<ColumnRefStar>(bin.Left);
        Assert.Equal("first_name", left.Name);
        var right = Assert.IsType<ColumnRefStar>(bin.Right);
        Assert.Equal("last_name", right.Name);
    }

    [Fact]
    public void Parse_StringConcatChained_LeftAssociative()
    {
        var expr = ParseExpr("a + ' ' + b");
        var outer = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Add, outer.Op);
        var inner = Assert.IsType<BinaryStar>(outer.Left);
        Assert.Equal(BinaryOp.Add, inner.Op);
    }

    // ─── $param references ───────────────────────────────────────────

    [Fact]
    public void Parse_Parameter_ReturnsParameterStar()
    {
        var expr = ParseExpr("$limit");
        var param = Assert.IsType<ParameterStar>(expr);
        Assert.Equal("limit", param.Name);
    }

    [Fact]
    public void Parse_ParameterInComparison_Works()
    {
        var expr = ParseExpr("age > $min_age");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.GreaterThan, bin.Op);
        var param = Assert.IsType<ParameterStar>(bin.Right);
        Assert.Equal("min_age", param.Name);
    }

    [Fact]
    public void Parse_ParameterInArithmetic_Works()
    {
        var expr = ParseExpr("$offset + 10");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Add, bin.Op);
        var param = Assert.IsType<ParameterStar>(bin.Left);
        Assert.Equal("offset", param.Name);
    }

    [Fact]
    public void Parse_MultipleParametersInExpression_Works()
    {
        var expr = ParseExpr("$a + $b");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.IsType<ParameterStar>(bin.Left);
        Assert.IsType<ParameterStar>(bin.Right);
    }

    // ─── Subqueries ─────────────────────────────────────────────────

    [Fact]
    public void Parse_ScalarSubquery_ReturnsSubqueryStar()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE x = (SELECT max(y) FROM u)");
        var where = Assert.IsType<BinaryStar>(stmt.Where);
        var sub = Assert.IsType<SubqueryStar>(where.Right);
        Assert.Equal("u", sub.Query.From.Name);
    }

    [Fact]
    public void Parse_InSubquery_ReturnsInSubqueryStar()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE id IN (SELECT id FROM u)");
        var inSub = Assert.IsType<InSubqueryStar>(stmt.Where);
        Assert.False(inSub.Negated);
        Assert.Equal("u", inSub.Query.From.Name);
    }

    [Fact]
    public void Parse_NotInSubquery_ReturnsNegatedInSubqueryStar()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE id NOT IN (SELECT id FROM u)");
        var inSub = Assert.IsType<InSubqueryStar>(stmt.Where);
        Assert.True(inSub.Negated);
    }

    [Fact]
    public void Parse_InExprList_StillWorks()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE x IN (1, 2, 3)");
        var inExpr = Assert.IsType<InStar>(stmt.Where);
        Assert.Equal(3, inExpr.Values.Count);
    }

    [Fact]
    public void Parse_ParenExpr_StillWorks()
    {
        var expr = ParseExpr("(1 + 2)");
        var bin = Assert.IsType<BinaryStar>(expr);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }

    [Fact]
    public void Parse_NestedSubquery_Works()
    {
        var stmt = SharqParser.Parse(
            "SELECT * FROM t WHERE x IN (SELECT id FROM u WHERE y > (SELECT min(z) FROM v))");
        var inSub = Assert.IsType<InSubqueryStar>(stmt.Where);
        var innerWhere = Assert.IsType<BinaryStar>(inSub.Query.Where);
        var nested = Assert.IsType<SubqueryStar>(innerWhere.Right);
        Assert.Equal("v", nested.Query.From.Name);
    }

    [Fact]
    public void Parse_SubqueryInSelectList_Works()
    {
        var stmt = SharqParser.Parse("SELECT (SELECT count(*) FROM u) AS cnt FROM t");
        Assert.Single(stmt.Columns);
        var sub = Assert.IsType<SubqueryStar>(stmt.Columns[0].Expression);
        Assert.Equal("u", sub.Query.From.Name);
        Assert.Equal("cnt", stmt.Columns[0].Alias);
    }

    // ─── EXISTS ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_Exists_ReturnsExistsStar()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE EXISTS (SELECT * FROM u)");
        var exists = Assert.IsType<ExistsStar>(stmt.Where);
        Assert.Equal("u", exists.Query.From.Name);
    }

    [Fact]
    public void Parse_NotExists_ReturnsUnaryNotWrappingExists()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE NOT EXISTS (SELECT * FROM u)");
        var unary = Assert.IsType<UnaryStar>(stmt.Where);
        Assert.Equal(UnaryOp.Not, unary.Op);
        var exists = Assert.IsType<ExistsStar>(unary.Operand);
        Assert.Equal("u", exists.Query.From.Name);
    }

    [Fact]
    public void Parse_ExistsWithComplexSubquery_Works()
    {
        var stmt = SharqParser.Parse(
            "SELECT * FROM t WHERE EXISTS (SELECT 1 FROM u WHERE u.id = 5 LIMIT 1)");
        var exists = Assert.IsType<ExistsStar>(stmt.Where);
        Assert.NotNull(exists.Query.Where);
        Assert.NotNull(exists.Query.Limit);
    }

    [Fact]
    public void Parse_ExistsCaseInsensitive_Works()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE exists (select * from u)");
        Assert.IsType<ExistsStar>(stmt.Where);
    }

    // ─── Pipe Exists Shorthand ──────────────────────────────────────

    [Fact]
    public void Parse_PipeExists_ReturnsExistsStar()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE |? (SELECT * FROM u)");
        var exists = Assert.IsType<ExistsStar>(stmt.Where);
        Assert.Equal("u", exists.Query.From.Name);
    }

    [Fact]
    public void Parse_NotPipeExists_ReturnsUnaryNotWrappingExists()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE NOT |? (SELECT * FROM u)");
        var unary = Assert.IsType<UnaryStar>(stmt.Where);
        Assert.Equal(UnaryOp.Not, unary.Op);
        Assert.IsType<ExistsStar>(unary.Operand);
    }

    [Fact]
    public void Parse_PipeExistsWithComplexSubquery_Works()
    {
        var stmt = SharqParser.Parse(
            "SELECT * FROM t WHERE |? (SELECT 1 FROM u WHERE u.id = 5 LIMIT 1)");
        var exists = Assert.IsType<ExistsStar>(stmt.Where);
        Assert.NotNull(exists.Query.Where);
        Assert.NotNull(exists.Query.Limit);
    }

    [Fact]
    public void Parse_PipeExistsAsExpression_Works()
    {
        var expr = ParseExpr("|? (SELECT * FROM u)");
        Assert.IsType<ExistsStar>(expr);
    }
}
