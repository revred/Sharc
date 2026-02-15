// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

public class SharqWindowFunctionTests
{
    private static SelectStatement Parse(string sql) => SharqParser.Parse(sql);

    // ─── Basic OVER ─────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyOver_ReturnsWindowStar()
    {
        var stmt = Parse("SELECT row_number() OVER () FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.Equal("row_number", win.Function.Name);
        Assert.Null(win.PartitionBy);
        Assert.Null(win.OrderBy);
        Assert.Null(win.Frame);
    }

    [Fact]
    public void Parse_OverPartitionBy_SetsPartitionBy()
    {
        var stmt = Parse("SELECT sum(x) OVER (PARTITION BY dept) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.PartitionBy);
        Assert.Single(win.PartitionBy);
        var col = Assert.IsType<ColumnRefStar>(win.PartitionBy[0]);
        Assert.Equal("dept", col.Name);
    }

    [Fact]
    public void Parse_OverOrderBy_SetsOrderBy()
    {
        var stmt = Parse("SELECT rank() OVER (ORDER BY score DESC) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.Null(win.PartitionBy);
        Assert.NotNull(win.OrderBy);
        Assert.Single(win.OrderBy);
        Assert.True(win.OrderBy[0].Descending);
    }

    [Fact]
    public void Parse_OverPartitionByAndOrderBy_SetsBoth()
    {
        var stmt = Parse("SELECT row_number() OVER (PARTITION BY dept ORDER BY hire_date) FROM emp");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.PartitionBy);
        Assert.Single(win.PartitionBy);
        Assert.NotNull(win.OrderBy);
        Assert.Single(win.OrderBy);
    }

    [Fact]
    public void Parse_MultiExprPartitionBy_Works()
    {
        var stmt = Parse("SELECT count(*) OVER (PARTITION BY a, b, c) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.PartitionBy);
        Assert.Equal(3, win.PartitionBy.Count);
    }

    [Fact]
    public void Parse_OrderByWithNullsInWindow_Works()
    {
        var stmt = Parse("SELECT sum(x) OVER (ORDER BY y DESC NULLS LAST) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.OrderBy);
        Assert.True(win.OrderBy[0].Descending);
        Assert.Equal(NullOrdering.NullsLast, win.OrderBy[0].NullOrdering);
    }

    // ─── Frame clauses ──────────────────────────────────────────────

    [Fact]
    public void Parse_RowsUnboundedPreceding_SetsFrame()
    {
        var stmt = Parse("SELECT sum(x) OVER (ORDER BY id ROWS UNBOUNDED PRECEDING) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(WindowFrameKind.Rows, win.Frame.Kind);
        Assert.Equal(FrameBoundKind.UnboundedPreceding, win.Frame.Start.Kind);
        Assert.Null(win.Frame.End);
    }

    [Fact]
    public void Parse_RangeCurrentRow_SetsFrame()
    {
        var stmt = Parse("SELECT avg(x) OVER (ORDER BY id RANGE CURRENT ROW) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(WindowFrameKind.Range, win.Frame.Kind);
        Assert.Equal(FrameBoundKind.CurrentRow, win.Frame.Start.Kind);
    }

    [Fact]
    public void Parse_RowsExprPreceding_SetsFrame()
    {
        var stmt = Parse("SELECT sum(x) OVER (ORDER BY id ROWS 3 PRECEDING) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(FrameBoundKind.ExprPreceding, win.Frame.Start.Kind);
        Assert.NotNull(win.Frame.Start.Offset);
        var lit = Assert.IsType<LiteralStar>(win.Frame.Start.Offset);
        Assert.Equal(3L, lit.IntegerValue);
    }

    [Fact]
    public void Parse_RowsBetweenBounds_SetsStartAndEnd()
    {
        var stmt = Parse("SELECT sum(x) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(WindowFrameKind.Rows, win.Frame.Kind);
        Assert.Equal(FrameBoundKind.UnboundedPreceding, win.Frame.Start.Kind);
        Assert.NotNull(win.Frame.End);
        Assert.Equal(FrameBoundKind.CurrentRow, win.Frame.End.Kind);
    }

    [Fact]
    public void Parse_RowsBetweenExprBounds_Works()
    {
        var stmt = Parse("SELECT sum(x) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 2 FOLLOWING) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(FrameBoundKind.ExprPreceding, win.Frame.Start.Kind);
        Assert.NotNull(win.Frame.Start.Offset);
        Assert.NotNull(win.Frame.End);
        Assert.Equal(FrameBoundKind.ExprFollowing, win.Frame.End.Kind);
        Assert.NotNull(win.Frame.End.Offset);
    }

    [Fact]
    public void Parse_RowsBetweenUnboundedPrecedingAndUnboundedFollowing_Works()
    {
        var stmt = Parse("SELECT sum(x) OVER (ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM t");
        var win = Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.NotNull(win.Frame);
        Assert.Equal(FrameBoundKind.UnboundedPreceding, win.Frame.Start.Kind);
        Assert.NotNull(win.Frame.End);
        Assert.Equal(FrameBoundKind.UnboundedFollowing, win.Frame.End.Kind);
    }

    // ─── Window in SELECT list ──────────────────────────────────────

    [Fact]
    public void Parse_WindowWithAlias_SetsAlias()
    {
        var stmt = Parse("SELECT row_number() OVER (ORDER BY id) AS rn FROM t");
        Assert.Equal("rn", stmt.Columns[0].Alias);
        Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
    }

    [Fact]
    public void Parse_MultipleWindows_AllParsed()
    {
        var stmt = Parse(
            "SELECT " +
            "  row_number() OVER (ORDER BY id) AS rn, " +
            "  sum(salary) OVER (PARTITION BY dept) AS dept_total " +
            "FROM emp");
        Assert.Equal(2, stmt.Columns.Count);
        Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
        Assert.IsType<WindowStar>(stmt.Columns[1].Expression);
    }

    [Fact]
    public void Parse_WindowCaseInsensitive_Works()
    {
        var stmt = Parse("select row_number() over (partition by dept order by id) from t");
        Assert.IsType<WindowStar>(stmt.Columns[0].Expression);
    }

    [Fact]
    public void Parse_WindowMissingCloseParen_Throws()
    {
        Assert.Throws<SharqParseException>(() =>
            Parse("SELECT sum(x) OVER (ORDER BY id FROM t"));
    }

    [Fact]
    public void Parse_FunctionWithoutOver_StillRegularFunction()
    {
        var stmt = Parse("SELECT count(*) FROM t");
        Assert.IsType<FunctionCallStar>(stmt.Columns[0].Expression);
    }

    // ─── Full integration ───────────────────────────────────────────

    [Fact]
    public void Parse_WindowFullStatement_AllCombined()
    {
        var stmt = Parse(
            "SELECT " +
            "  dept, " +
            "  name, " +
            "  salary, " +
            "  rank() OVER (PARTITION BY dept ORDER BY salary DESC) AS dept_rank, " +
            "  sum(salary) OVER (PARTITION BY dept ORDER BY salary ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total " +
            "FROM employees " +
            "WHERE active = 1 " +
            "ORDER BY dept, dept_rank");

        Assert.Equal(5, stmt.Columns.Count);

        // rank() window
        var rank = Assert.IsType<WindowStar>(stmt.Columns[3].Expression);
        Assert.Equal("rank", rank.Function.Name);
        Assert.NotNull(rank.PartitionBy);
        Assert.NotNull(rank.OrderBy);
        Assert.Null(rank.Frame);
        Assert.Equal("dept_rank", stmt.Columns[3].Alias);

        // sum() window with frame
        var running = Assert.IsType<WindowStar>(stmt.Columns[4].Expression);
        Assert.Equal("sum", running.Function.Name);
        Assert.NotNull(running.Frame);
        Assert.Equal(WindowFrameKind.Rows, running.Frame.Kind);
        Assert.Equal(FrameBoundKind.UnboundedPreceding, running.Frame.Start.Kind);
        Assert.NotNull(running.Frame.End);
        Assert.Equal(FrameBoundKind.CurrentRow, running.Frame.End.Kind);
    }
}
