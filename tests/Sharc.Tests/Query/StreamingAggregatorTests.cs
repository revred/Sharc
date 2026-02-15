// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Tests.Query;

public class StreamingAggregatorTests
{
    private static QueryValue[] Row(long id, string dept, long salary) =>
        [QueryValue.FromInt64(id), QueryValue.FromString(dept), QueryValue.FromInt64(salary)];

    private static QueryValue[] Row(long id, string dept, double salary) =>
        [QueryValue.FromInt64(id), QueryValue.FromString(dept), QueryValue.FromDouble(salary)];

    private static readonly string[] SourceColumns = ["id", "dept", "salary"];

    // ─── COUNT(*) ────────────────────────────────────────────────

    [Fact]
    public void CountStar_NoGroupBy_ReturnsRowCount()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.CountStar, Alias = "cnt", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200));
        agg.AccumulateRow(Row(3, "sales", 150));

        var (rows, cols) = agg.Finalize();
        Assert.Single(rows);
        Assert.Equal(3L, rows[0][0].AsInt64());
    }

    [Fact]
    public void CountStar_WithGroupBy_ReturnsPerGroupCount()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.CountStar, Alias = "cnt", OutputOrdinal = 1 }],
            ["dept"],
            ["dept", "cnt"]);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200));
        agg.AccumulateRow(Row(3, "sales", 150));

        var (rows, cols) = agg.Finalize();
        Assert.Equal(2, rows.Count);

        var eng = rows.First(r => r[0].AsString() == "eng");
        var sales = rows.First(r => r[0].AsString() == "sales");
        Assert.Equal(2L, eng[1].AsInt64());
        Assert.Equal(1L, sales[1].AsInt64());
    }

    // ─── COUNT(column) ───────────────────────────────────────────

    [Fact]
    public void Count_SkipsNulls()
    {
        var columns = new[] { "val" };
        var agg = new StreamingAggregator(
            columns,
            [new AggregateIntent { Function = AggregateFunction.Count, ColumnName = "val", Alias = "cnt", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow([QueryValue.FromInt64(1)]);
        agg.AccumulateRow([QueryValue.Null]);
        agg.AccumulateRow([QueryValue.FromInt64(3)]);

        var (rows, _) = agg.Finalize();
        Assert.Equal(2L, rows[0][0].AsInt64());
    }

    // ─── SUM ─────────────────────────────────────────────────────

    [Fact]
    public void Sum_Int_ReturnsInt64()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.Sum, ColumnName = "salary", Alias = "total", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200));
        agg.AccumulateRow(Row(3, "sales", 150));

        var (rows, _) = agg.Finalize();
        Assert.Equal(QueryValueType.Int64, rows[0][0].Type);
        Assert.Equal(450L, rows[0][0].AsInt64());
    }

    [Fact]
    public void Sum_Mixed_ReturnsDouble()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.Sum, ColumnName = "salary", Alias = "total", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200.5));

        var (rows, _) = agg.Finalize();
        Assert.Equal(QueryValueType.Double, rows[0][0].Type);
        Assert.Equal(300.5, rows[0][0].AsDouble());
    }

    // ─── AVG ─────────────────────────────────────────────────────

    [Fact]
    public void Avg_ReturnsDouble()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.Avg, ColumnName = "salary", Alias = "avg_sal", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200));
        agg.AccumulateRow(Row(3, "sales", 300));

        var (rows, _) = agg.Finalize();
        Assert.Equal(200.0, rows[0][0].AsDouble());
    }

    // ─── MIN / MAX ───────────────────────────────────────────────

    [Fact]
    public void Min_ReturnsSmallest()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.Min, ColumnName = "salary", Alias = "min_sal", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 200));
        agg.AccumulateRow(Row(2, "eng", 100));
        agg.AccumulateRow(Row(3, "sales", 300));

        var (rows, _) = agg.Finalize();
        Assert.Equal(100L, rows[0][0].AsInt64());
    }

    [Fact]
    public void Max_ReturnsLargest()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [new AggregateIntent { Function = AggregateFunction.Max, ColumnName = "salary", Alias = "max_sal", OutputOrdinal = 0 }],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 200));
        agg.AccumulateRow(Row(2, "eng", 100));
        agg.AccumulateRow(Row(3, "sales", 300));

        var (rows, _) = agg.Finalize();
        Assert.Equal(300L, rows[0][0].AsInt64());
    }

    // ─── Multiple groups ─────────────────────────────────────────

    [Fact]
    public void MultipleGroups_AllCorrect()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [
                new AggregateIntent { Function = AggregateFunction.CountStar, Alias = "cnt", OutputOrdinal = 1 },
                new AggregateIntent { Function = AggregateFunction.Sum, ColumnName = "salary", Alias = "total", OutputOrdinal = 2 },
            ],
            ["dept"],
            ["dept", "cnt", "total"]);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 200));
        agg.AccumulateRow(Row(3, "sales", 150));
        agg.AccumulateRow(Row(4, "sales", 250));
        agg.AccumulateRow(Row(5, "sales", 300));

        var (rows, cols) = agg.Finalize();
        Assert.Equal(2, rows.Count);

        var eng = rows.First(r => r[0].AsString() == "eng");
        Assert.Equal(2L, eng[1].AsInt64());
        Assert.Equal(300L, eng[2].AsInt64());

        var sales = rows.First(r => r[0].AsString() == "sales");
        Assert.Equal(3L, sales[1].AsInt64());
        Assert.Equal(700L, sales[2].AsInt64());
    }

    // ─── No GROUP BY, single accumulator ─────────────────────────

    [Fact]
    public void NoGroupBy_MultipleAggregates_SingleRow()
    {
        var agg = new StreamingAggregator(
            SourceColumns,
            [
                new AggregateIntent { Function = AggregateFunction.CountStar, Alias = "cnt", OutputOrdinal = 0 },
                new AggregateIntent { Function = AggregateFunction.Avg, ColumnName = "salary", Alias = "avg", OutputOrdinal = 1 },
                new AggregateIntent { Function = AggregateFunction.Min, ColumnName = "salary", Alias = "min", OutputOrdinal = 2 },
                new AggregateIntent { Function = AggregateFunction.Max, ColumnName = "salary", Alias = "max", OutputOrdinal = 3 },
            ],
            null, null);

        agg.AccumulateRow(Row(1, "eng", 100));
        agg.AccumulateRow(Row(2, "eng", 300));

        var (rows, _) = agg.Finalize();
        Assert.Single(rows);
        Assert.Equal(2L, rows[0][0].AsInt64());
        Assert.Equal(200.0, rows[0][1].AsDouble());
        Assert.Equal(100L, rows[0][2].AsInt64());
        Assert.Equal(300L, rows[0][3].AsInt64());
    }

    // ─── Matches materialized path ───────────────────────────────

    [Fact]
    public void StreamingAggregator_MatchesMaterializedPath()
    {
        // Compare streaming vs materialized AggregateProcessor for same data
        var sourceRows = new List<QueryValue[]>
        {
            Row(1, "eng", 100),
            Row(2, "eng", 200),
            Row(3, "sales", 150),
            Row(4, "hr", 180),
            Row(5, "sales", 250),
        };

        var aggregates = new AggregateIntent[]
        {
            new() { Function = AggregateFunction.CountStar, Alias = "cnt", OutputOrdinal = 1 },
            new() { Function = AggregateFunction.Sum, ColumnName = "salary", Alias = "total", OutputOrdinal = 2 },
            new() { Function = AggregateFunction.Avg, ColumnName = "salary", Alias = "avg", OutputOrdinal = 3 },
        };
        var groupBy = new[] { "dept" };
        var outCols = new[] { "dept", "cnt", "total", "avg" };

        // Materialized path
        var (matRows, matCols) = AggregateProcessor.Apply(
            new List<QueryValue[]>(sourceRows), SourceColumns,
            aggregates, groupBy, outCols);

        // Streaming path
        var streaming = new StreamingAggregator(SourceColumns, aggregates, groupBy, outCols);
        foreach (var row in sourceRows)
            streaming.AccumulateRow(row);
        var (strRows, strCols) = streaming.Finalize();

        // Same number of groups
        Assert.Equal(matRows.Count, strRows.Count);

        // Compare results (order may differ, so match by dept name)
        foreach (var matRow in matRows)
        {
            string dept = matRow[0].AsString();
            var strRow = strRows.First(r => r[0].AsString() == dept);

            Assert.Equal(matRow[1].AsInt64(), strRow[1].AsInt64());   // cnt
            Assert.Equal(matRow[2].AsInt64(), strRow[2].AsInt64());   // total
            Assert.Equal(matRow[3].AsDouble(), strRow[3].AsDouble()); // avg
        }
    }
}
