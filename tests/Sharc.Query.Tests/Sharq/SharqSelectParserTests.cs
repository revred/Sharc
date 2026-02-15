// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

public class SharqSelectParserTests
{
    private static SelectStatement Parse(string sql) => SharqParser.Parse(sql);

    [Fact]
    public void Parse_SelectStar_ReturnsWildcard()
    {
        var stmt = Parse("SELECT * FROM users");
        Assert.Single(stmt.Columns);
        Assert.IsType<WildcardStar>(stmt.Columns[0].Expression);
    }

    [Fact]
    public void Parse_SelectColumns_ReturnsColumnList()
    {
        var stmt = Parse("SELECT id, name, age FROM users");
        Assert.Equal(3, stmt.Columns.Count);
        var col0 = Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression);
        Assert.Equal("id", col0.Name);
        var col1 = Assert.IsType<ColumnRefStar>(stmt.Columns[1].Expression);
        Assert.Equal("name", col1.Name);
        var col2 = Assert.IsType<ColumnRefStar>(stmt.Columns[2].Expression);
        Assert.Equal("age", col2.Name);
    }

    [Fact]
    public void Parse_SelectWithAliases_SetsAliasNames()
    {
        var stmt = Parse("SELECT name AS user_name, age AS user_age FROM users");
        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal("user_name", stmt.Columns[0].Alias);
        Assert.Equal("user_age", stmt.Columns[1].Alias);
    }

    [Fact]
    public void Parse_FromTable_SetsTableRef()
    {
        var stmt = Parse("SELECT * FROM users");
        Assert.Equal("users", stmt.From.Name);
        Assert.Null(stmt.From.Alias);
    }

    [Fact]
    public void Parse_FromTableWithAlias_SetsTableAlias()
    {
        var stmt = Parse("SELECT * FROM users AS u");
        Assert.Equal("users", stmt.From.Name);
        Assert.Equal("u", stmt.From.Alias);
    }

    [Fact]
    public void Parse_WhereClause_SetsWhereExpression()
    {
        var stmt = Parse("SELECT * FROM users WHERE age > 18");
        Assert.NotNull(stmt.Where);
        var bin = Assert.IsType<BinaryStar>(stmt.Where);
        Assert.Equal(BinaryOp.GreaterThan, bin.Op);
    }

    [Fact]
    public void Parse_GroupBy_SetsGroupByList()
    {
        var stmt = Parse("SELECT department, count(*) FROM employees GROUP BY department");
        Assert.NotNull(stmt.GroupBy);
        Assert.Single(stmt.GroupBy);
        var col = Assert.IsType<ColumnRefStar>(stmt.GroupBy[0]);
        Assert.Equal("department", col.Name);
    }

    [Fact]
    public void Parse_GroupByMultiple_SetsGroupByList()
    {
        var stmt = Parse("SELECT a, b, count(*) FROM t GROUP BY a, b");
        Assert.NotNull(stmt.GroupBy);
        Assert.Equal(2, stmt.GroupBy.Count);
    }

    [Fact]
    public void Parse_OrderByAsc_SetsOrderDirection()
    {
        var stmt = Parse("SELECT * FROM users ORDER BY name ASC");
        Assert.NotNull(stmt.OrderBy);
        Assert.Single(stmt.OrderBy);
        Assert.False(stmt.OrderBy[0].Descending);
    }

    [Fact]
    public void Parse_OrderByDesc_SetsOrderDirection()
    {
        var stmt = Parse("SELECT * FROM users ORDER BY age DESC");
        Assert.NotNull(stmt.OrderBy);
        Assert.Single(stmt.OrderBy);
        Assert.True(stmt.OrderBy[0].Descending);
    }

    [Fact]
    public void Parse_OrderByDefaultAsc_WhenNoDirection()
    {
        var stmt = Parse("SELECT * FROM users ORDER BY name");
        Assert.NotNull(stmt.OrderBy);
        Assert.False(stmt.OrderBy[0].Descending);
    }

    [Fact]
    public void Parse_OrderByMultiple_ReturnsOrderedList()
    {
        var stmt = Parse("SELECT * FROM users ORDER BY age DESC, name ASC");
        Assert.NotNull(stmt.OrderBy);
        Assert.Equal(2, stmt.OrderBy.Count);
        Assert.True(stmt.OrderBy[0].Descending);
        Assert.False(stmt.OrderBy[1].Descending);
    }

    // ─── NULLS FIRST/LAST ─────────────────────────────────────────────

    [Fact]
    public void Parse_OrderByNullsFirst_SetsNullOrdering()
    {
        var stmt = Parse("SELECT * FROM t ORDER BY name NULLS FIRST");
        Assert.NotNull(stmt.OrderBy);
        Assert.Equal(NullOrdering.NullsFirst, stmt.OrderBy[0].NullOrdering);
    }

    [Fact]
    public void Parse_OrderByDescNullsLast_SetsNullOrdering()
    {
        var stmt = Parse("SELECT * FROM t ORDER BY age DESC NULLS LAST");
        Assert.NotNull(stmt.OrderBy);
        Assert.True(stmt.OrderBy[0].Descending);
        Assert.Equal(NullOrdering.NullsLast, stmt.OrderBy[0].NullOrdering);
    }

    [Fact]
    public void Parse_OrderByNoNulls_NullOrderingIsNull()
    {
        var stmt = Parse("SELECT * FROM t ORDER BY name ASC");
        Assert.NotNull(stmt.OrderBy);
        Assert.Null(stmt.OrderBy[0].NullOrdering);
    }

    [Fact]
    public void Parse_OrderByMultipleWithNulls_Works()
    {
        var stmt = Parse("SELECT * FROM t ORDER BY a DESC NULLS LAST, b ASC NULLS FIRST");
        Assert.NotNull(stmt.OrderBy);
        Assert.Equal(2, stmt.OrderBy.Count);
        Assert.Equal(NullOrdering.NullsLast, stmt.OrderBy[0].NullOrdering);
        Assert.Equal(NullOrdering.NullsFirst, stmt.OrderBy[1].NullOrdering);
    }

    [Fact]
    public void Parse_NullsWithoutFirstOrLast_Throws()
    {
        Assert.Throws<SharqParseException>(() =>
            Parse("SELECT * FROM t ORDER BY name NULLS BOGUS"));
    }

    // ─── LIMIT / OFFSET ─────────────────────────────────────────────

    [Fact]
    public void Parse_Limit_SetsLimitExpr()
    {
        var stmt = Parse("SELECT * FROM users LIMIT 10");
        Assert.NotNull(stmt.Limit);
        var lit = Assert.IsType<LiteralStar>(stmt.Limit);
        Assert.Equal(10L, lit.IntegerValue);
        Assert.Null(stmt.Offset);
    }

    [Fact]
    public void Parse_LimitOffset_SetsBothExprs()
    {
        var stmt = Parse("SELECT * FROM users LIMIT 10 OFFSET 20");
        Assert.NotNull(stmt.Limit);
        Assert.NotNull(stmt.Offset);
        var lim = Assert.IsType<LiteralStar>(stmt.Limit);
        Assert.Equal(10L, lim.IntegerValue);
        var off = Assert.IsType<LiteralStar>(stmt.Offset);
        Assert.Equal(20L, off.IntegerValue);
    }

    [Fact]
    public void Parse_FullQuery_AllClausesCombined()
    {
        var stmt = Parse(
            "SELECT department, count(*) AS total " +
            "FROM employees " +
            "WHERE status = 'active' " +
            "GROUP BY department " +
            "ORDER BY total DESC " +
            "LIMIT 10 OFFSET 0");

        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal("total", stmt.Columns[1].Alias);
        Assert.Equal("employees", stmt.From.Name);
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.GroupBy);
        Assert.Single(stmt.GroupBy);
        Assert.NotNull(stmt.OrderBy);
        Assert.Single(stmt.OrderBy);
        Assert.True(stmt.OrderBy[0].Descending);
        Assert.NotNull(stmt.Limit);
        Assert.NotNull(stmt.Offset);
    }

    [Fact]
    public void Parse_ExpressionInSelectList_ComputedColumns()
    {
        var stmt = Parse("SELECT count(*) AS total FROM users");
        var fn = Assert.IsType<FunctionCallStar>(stmt.Columns[0].Expression);
        Assert.Equal("count", fn.Name);
        Assert.True(fn.IsStarArg);
        Assert.Equal("total", stmt.Columns[0].Alias);
    }

    [Fact]
    public void Parse_CaseInsensitiveKeywords_Works()
    {
        var stmt = Parse("select * from users where age > 0");
        Assert.Single(stmt.Columns);
        Assert.Equal("users", stmt.From.Name);
        Assert.NotNull(stmt.Where);
    }

    [Fact]
    public void Parse_TrailingSemicolon_Accepted()
    {
        var stmt = Parse("SELECT * FROM users;");
        Assert.Equal("users", stmt.From.Name);
    }

    [Fact]
    public void Parse_FromRecordId_ParsesTableAndId()
    {
        var stmt = Parse("SELECT * FROM person:alice");
        Assert.Equal("person", stmt.From.Name);
        Assert.Equal("alice", stmt.From.RecordId);
    }

    [Fact]
    public void Parse_WhereWithComplex_ParsesCorrectly()
    {
        var stmt = Parse("SELECT * FROM users WHERE age >= 18 AND status = 'active'");
        var and = Assert.IsType<BinaryStar>(stmt.Where);
        Assert.Equal(BinaryOp.And, and.Op);
    }

    // ─── SELECT DISTINCT ─────────────────────────────────────────────

    [Fact]
    public void Parse_SelectDistinct_SetsIsDistinct()
    {
        var stmt = Parse("SELECT DISTINCT name FROM users");
        Assert.True(stmt.IsDistinct);
        Assert.Single(stmt.Columns);
        var col = Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression);
        Assert.Equal("name", col.Name);
    }

    [Fact]
    public void Parse_SelectDistinctStar_Works()
    {
        var stmt = Parse("SELECT DISTINCT * FROM users");
        Assert.True(stmt.IsDistinct);
        Assert.IsType<WildcardStar>(stmt.Columns[0].Expression);
    }

    [Fact]
    public void Parse_SelectWithoutDistinct_IsDistinctFalse()
    {
        var stmt = Parse("SELECT * FROM users");
        Assert.False(stmt.IsDistinct);
    }

    [Fact]
    public void Parse_SelectDistinctMultipleColumns_Works()
    {
        var stmt = Parse("SELECT DISTINCT name, city FROM users");
        Assert.True(stmt.IsDistinct);
        Assert.Equal(2, stmt.Columns.Count);
    }

    [Fact]
    public void Parse_SelectDistinctCaseInsensitive_Works()
    {
        var stmt = Parse("select distinct name from users");
        Assert.True(stmt.IsDistinct);
    }

    // ─── HAVING ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_GroupByHaving_SetsHavingExpr()
    {
        var stmt = Parse("SELECT department, count(*) AS cnt FROM employees GROUP BY department HAVING count(*) > 5");
        Assert.NotNull(stmt.GroupBy);
        Assert.NotNull(stmt.Having);
        var bin = Assert.IsType<BinaryStar>(stmt.Having);
        Assert.Equal(BinaryOp.GreaterThan, bin.Op);
        var fn = Assert.IsType<FunctionCallStar>(bin.Left);
        Assert.Equal("count", fn.Name);
    }

    [Fact]
    public void Parse_GroupByWithoutHaving_HavingIsNull()
    {
        var stmt = Parse("SELECT department FROM employees GROUP BY department");
        Assert.NotNull(stmt.GroupBy);
        Assert.Null(stmt.Having);
    }

    [Fact]
    public void Parse_HavingWithComplexExpr_Works()
    {
        var stmt = Parse("SELECT city, avg(age) FROM people GROUP BY city HAVING avg(age) >= 21 AND count(*) > 10");
        Assert.NotNull(stmt.Having);
        var and = Assert.IsType<BinaryStar>(stmt.Having);
        Assert.Equal(BinaryOp.And, and.Op);
    }

    [Fact]
    public void Parse_HavingOrderByLimitCombined_Works()
    {
        var stmt = Parse(
            "SELECT dept, sum(salary) AS total FROM emp " +
            "GROUP BY dept HAVING sum(salary) > 100000 " +
            "ORDER BY total DESC LIMIT 10");
        Assert.NotNull(stmt.GroupBy);
        Assert.NotNull(stmt.Having);
        Assert.NotNull(stmt.OrderBy);
        Assert.NotNull(stmt.Limit);
    }

    // ─── CASE in SELECT list ─────────────────────────────────────────

    [Fact]
    public void Parse_CaseInSelectList_Works()
    {
        var stmt = Parse(
            "SELECT name, CASE WHEN age >= 18 THEN 'adult' ELSE 'minor' END AS category FROM users");
        Assert.Equal(2, stmt.Columns.Count);
        var caseExpr = Assert.IsType<CaseStar>(stmt.Columns[1].Expression);
        Assert.Single(caseExpr.Whens);
        Assert.Equal("category", stmt.Columns[1].Alias);
    }

    // ─── CAST in SELECT list ─────────────────────────────────────────

    [Fact]
    public void Parse_CastInSelectList_Works()
    {
        var stmt = Parse("SELECT CAST(age AS TEXT) AS age_text FROM users");
        Assert.Single(stmt.Columns);
        var cast = Assert.IsType<CastStar>(stmt.Columns[0].Expression);
        Assert.Equal("TEXT", cast.TypeName);
        Assert.Equal("age_text", stmt.Columns[0].Alias);
    }

    // ─── String concat via + in SELECT list ─────────────────────────

    [Fact]
    public void Parse_ConcatInSelectList_Works()
    {
        var stmt = Parse("SELECT first_name + ' ' + last_name AS full_name FROM users");
        Assert.Single(stmt.Columns);
        var bin = Assert.IsType<BinaryStar>(stmt.Columns[0].Expression);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.Equal("full_name", stmt.Columns[0].Alias);
    }

    // ─── $param in WHERE ─────────────────────────────────────────────

    [Fact]
    public void Parse_ParameterInWhere_Works()
    {
        var stmt = Parse("SELECT * FROM users WHERE age > $min_age");
        Assert.NotNull(stmt.Where);
        var bin = Assert.IsType<BinaryStar>(stmt.Where);
        var param = Assert.IsType<ParameterStar>(bin.Right);
        Assert.Equal("min_age", param.Name);
    }

    [Fact]
    public void Parse_ParameterInLimitOffset_Works()
    {
        var stmt = Parse("SELECT * FROM users LIMIT $page_size OFFSET $skip");
        Assert.NotNull(stmt.Limit);
        Assert.NotNull(stmt.Offset);
        var limit = Assert.IsType<ParameterStar>(stmt.Limit);
        Assert.Equal("page_size", limit.Name);
        var offset = Assert.IsType<ParameterStar>(stmt.Offset);
        Assert.Equal("skip", offset.Name);
    }

    // ─── Full integration: all 6 features combined ───────────────────

    [Fact]
    public void Parse_AllNewFeaturesCombined_Works()
    {
        var stmt = Parse(
            "SELECT DISTINCT " +
            "  department, " +
            "  CASE WHEN count(*) > 10 THEN 'large' ELSE 'small' END AS size, " +
            "  CAST(avg(salary) AS INTEGER) AS avg_salary, " +
            "  first_name + ' ' + last_name AS full_name " +
            "FROM employees " +
            "WHERE status = $active_status " +
            "GROUP BY department " +
            "HAVING count(*) > $min_count " +
            "ORDER BY avg_salary DESC " +
            "LIMIT $page_size");

        Assert.True(stmt.IsDistinct);
        Assert.Equal(4, stmt.Columns.Count);
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.GroupBy);
        Assert.NotNull(stmt.Having);
        Assert.NotNull(stmt.OrderBy);
        Assert.NotNull(stmt.Limit);

        // Verify CASE in select
        Assert.IsType<CaseStar>(stmt.Columns[1].Expression);
        Assert.Equal("size", stmt.Columns[1].Alias);

        // Verify CAST in select
        Assert.IsType<CastStar>(stmt.Columns[2].Expression);

        // Verify + concat in select
        var concat = Assert.IsType<BinaryStar>(stmt.Columns[3].Expression);
        Assert.Equal(BinaryOp.Add, concat.Op);

        // Verify $param in WHERE
        var where = Assert.IsType<BinaryStar>(stmt.Where);
        Assert.IsType<ParameterStar>(where.Right);

        // Verify $param in HAVING
        var having = Assert.IsType<BinaryStar>(stmt.Having);
        Assert.IsType<ParameterStar>(having.Right);

        // Verify $param in LIMIT
        Assert.IsType<ParameterStar>(stmt.Limit);
    }

    // ─── CTEs (WITH ... AS) ─────────────────────────────────────────

    [Fact]
    public void Parse_SingleCte_SetsCteList()
    {
        var stmt = Parse("WITH active AS (SELECT * FROM users WHERE active = 1) SELECT * FROM active");
        Assert.NotNull(stmt.Ctes);
        Assert.Single(stmt.Ctes);
        Assert.Equal("active", stmt.Ctes[0].Name);
        Assert.Equal("users", stmt.Ctes[0].Query.From.Name);
    }

    [Fact]
    public void Parse_MultipleCtes_ParsesAll()
    {
        var stmt = Parse(
            "WITH a AS (SELECT * FROM t1), b AS (SELECT * FROM t2) SELECT * FROM a");
        Assert.NotNull(stmt.Ctes);
        Assert.Equal(2, stmt.Ctes.Count);
        Assert.Equal("a", stmt.Ctes[0].Name);
        Assert.Equal("b", stmt.Ctes[1].Name);
        Assert.Equal("t1", stmt.Ctes[0].Query.From.Name);
        Assert.Equal("t2", stmt.Ctes[1].Query.From.Name);
    }

    [Fact]
    public void Parse_CteInnerQueryParsed_Works()
    {
        var stmt = Parse("WITH top5 AS (SELECT * FROM users ORDER BY score DESC LIMIT 5) SELECT name FROM top5");
        Assert.NotNull(stmt.Ctes);
        Assert.NotNull(stmt.Ctes[0].Query.OrderBy);
        Assert.NotNull(stmt.Ctes[0].Query.Limit);
    }

    [Fact]
    public void Parse_NoCte_CtesIsNull()
    {
        var stmt = Parse("SELECT * FROM users");
        Assert.Null(stmt.Ctes);
    }

    [Fact]
    public void Parse_CteCaseInsensitive_Works()
    {
        var stmt = Parse("with x as (select * from t) select * from x");
        Assert.NotNull(stmt.Ctes);
        Assert.Single(stmt.Ctes);
    }

    [Fact]
    public void Parse_CteWithWhereGroupBy_Works()
    {
        var stmt = Parse(
            "WITH dept_stats AS (SELECT dept, count(*) AS cnt FROM emp GROUP BY dept HAVING count(*) > 3) " +
            "SELECT * FROM dept_stats WHERE cnt > 5 ORDER BY cnt DESC");
        Assert.NotNull(stmt.Ctes);
        Assert.NotNull(stmt.Ctes[0].Query.GroupBy);
        Assert.NotNull(stmt.Ctes[0].Query.Having);
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.OrderBy);
    }

    [Fact]
    public void Parse_CteMissingAs_Throws()
    {
        Assert.Throws<SharqParseException>(() =>
            Parse("WITH x (SELECT * FROM t) SELECT * FROM x"));
    }

    [Fact]
    public void Parse_CteMissingParen_Throws()
    {
        Assert.Throws<SharqParseException>(() =>
            Parse("WITH x AS SELECT * FROM t SELECT * FROM x"));
    }

    // ─── UNION / INTERSECT / EXCEPT ────────────────────────────────

    [Fact]
    public void Parse_Union_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 UNION SELECT * FROM t2");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
        Assert.Equal("t2", stmt.CompoundRight.From.Name);
    }

    [Fact]
    public void Parse_UnionAll_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 UNION ALL SELECT * FROM t2");
        Assert.Equal(CompoundOp.UnionAll, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_Intersect_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 INTERSECT SELECT * FROM t2");
        Assert.Equal(CompoundOp.Intersect, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_Except_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 EXCEPT SELECT * FROM t2");
        Assert.Equal(CompoundOp.Except, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_ThreeWayUnion_ChainsRight()
    {
        var stmt = Parse("SELECT * FROM t1 UNION SELECT * FROM t2 UNION SELECT * FROM t3");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
        Assert.Equal(CompoundOp.Union, stmt.CompoundRight.CompoundOp);
        Assert.NotNull(stmt.CompoundRight.CompoundRight);
        Assert.Equal("t3", stmt.CompoundRight.CompoundRight.From.Name);
    }

    [Fact]
    public void Parse_UnionBothSidesWithWhere_Works()
    {
        var stmt = Parse("SELECT * FROM t1 WHERE x > 1 UNION SELECT * FROM t2 WHERE y < 5");
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.CompoundRight);
        Assert.NotNull(stmt.CompoundRight.Where);
    }

    [Fact]
    public void Parse_UnionCaseInsensitive_Works()
    {
        var stmt = Parse("select * from t1 union select * from t2");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
    }

    [Fact]
    public void Parse_NoCompound_CompoundOpIsNull()
    {
        var stmt = Parse("SELECT * FROM users");
        Assert.Null(stmt.CompoundOp);
        Assert.Null(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_CompoundWithCte_Works()
    {
        var stmt = Parse("WITH a AS (SELECT * FROM t1) SELECT * FROM a UNION SELECT * FROM t2");
        Assert.NotNull(stmt.Ctes);
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_UnionWithOrderByOnOuter_Works()
    {
        var stmt = Parse("SELECT name FROM t1 UNION SELECT name FROM t2 ORDER BY name");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        // ORDER BY belongs to the rightmost (last) statement in the chain
        // or we can attach it to the compound — for simplicity, the rightmost gets it
        Assert.NotNull(stmt.CompoundRight);
    }

    // ─── Pipe Shorthand Compound Operators ──────────────────────────

    [Fact]
    public void Parse_PipeUnion_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 |u SELECT * FROM t2");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
        Assert.Equal("t2", stmt.CompoundRight.From.Name);
    }

    [Fact]
    public void Parse_PipeUnionAll_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 |a SELECT * FROM t2");
        Assert.Equal(CompoundOp.UnionAll, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_PipeIntersect_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 |n SELECT * FROM t2");
        Assert.Equal(CompoundOp.Intersect, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_PipeExcept_SetsCompoundOp()
    {
        var stmt = Parse("SELECT * FROM t1 |x SELECT * FROM t2");
        Assert.Equal(CompoundOp.Except, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
    }

    [Fact]
    public void Parse_PipeUnionChained_ChainsRight()
    {
        var stmt = Parse("SELECT * FROM t1 |u SELECT * FROM t2 |u SELECT * FROM t3");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
        Assert.Equal(CompoundOp.Union, stmt.CompoundRight.CompoundOp);
        Assert.Equal("t3", stmt.CompoundRight.CompoundRight!.From.Name);
    }

    [Fact]
    public void Parse_PipeUnionWithWhere_Works()
    {
        var stmt = Parse("SELECT * FROM t1 WHERE x > 1 |u SELECT * FROM t2 WHERE y < 5");
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.CompoundRight);
        Assert.NotNull(stmt.CompoundRight.Where);
    }

    [Fact]
    public void Parse_PipeMixedWithKeyword_Works()
    {
        var stmt = Parse("SELECT * FROM t1 |u SELECT * FROM t2 UNION ALL SELECT * FROM t3");
        Assert.Equal(CompoundOp.Union, stmt.CompoundOp);
        Assert.NotNull(stmt.CompoundRight);
        Assert.Equal(CompoundOp.UnionAll, stmt.CompoundRight.CompoundOp);
    }

    [Fact]
    public void Parse_PipeUnionAllEqualsKeywordUnionAll_SameAst()
    {
        var pipe = Parse("SELECT * FROM t1 |a SELECT * FROM t2");
        var keyword = Parse("SELECT * FROM t1 UNION ALL SELECT * FROM t2");
        Assert.Equal(keyword.CompoundOp, pipe.CompoundOp);
        Assert.Equal(keyword.CompoundRight!.From.Name, pipe.CompoundRight!.From.Name);
    }
}
