// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Query;

public class ScoredTopKProcessorTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Builds a materialized reader from (x, y) double pairs.</summary>
    private static SharcDataReader MakeReader(params (double x, double y)[] points)
    {
        var rows = new QueryValue[points.Length][];
        for (int i = 0; i < points.Length; i++)
            rows[i] = [QueryValue.FromDouble(points[i].x), QueryValue.FromDouble(points[i].y)];
        return new SharcDataReader(rows, ["x", "y"]);
    }

    /// <summary>Builds a materialized reader from integer values.</summary>
    private static SharcDataReader MakeIntReader(params long[] values)
    {
        var rows = new QueryValue[values.Length][];
        for (int i = 0; i < values.Length; i++)
            rows[i] = [QueryValue.FromInt64(values[i])];
        return new SharcDataReader(rows, ["val"]);
    }

    /// <summary>Scorer: Euclidean distance from a target (cx, cy).</summary>
    private sealed class DistanceScorer(double cx, double cy) : IRowScorer
    {
        public double Score(IRowAccessor row) =>
            Math.Sqrt(Math.Pow(row.GetDouble(0) - cx, 2) + Math.Pow(row.GetDouble(1) - cy, 2));
    }

    /// <summary>Scorer: absolute value of an integer column.</summary>
    private sealed class AbsScorer : IRowScorer
    {
        public double Score(IRowAccessor row) => Math.Abs(row.GetInt64(0));
    }

    // ── Core behavior ───────────────────────────────────────────────

    [Fact]
    public void Apply_ReturnsKNearestByScore()
    {
        // 5 points, find 2 nearest to origin (0,0)
        var reader = MakeReader(
            (10, 10),  // distance ~14.14
            (1, 1),    // distance ~1.41  -- nearest
            (5, 5),    // distance ~7.07
            (2, 0),    // distance  2.00  -- 2nd nearest
            (8, 8));   // distance ~11.31

        var result = ScoredTopKProcessor.Apply(reader, 2, new DistanceScorer(0, 0));

        var rows = ReadAll(result);
        Assert.Equal(2, rows.Count);
        // Should be (1,1) then (2,0) - sorted ascending by distance
        Assert.Equal(1.0, rows[0][0].AsDouble());
        Assert.Equal(1.0, rows[0][1].AsDouble());
        Assert.Equal(2.0, rows[1][0].AsDouble());
        Assert.Equal(0.0, rows[1][1].AsDouble());
    }

    [Fact]
    public void Apply_ResultsSortedAscendingByScore()
    {
        var reader = MakeIntReader(50, -3, 10, -1, 20, 2);
        var result = ScoredTopKProcessor.Apply(reader, 4, new AbsScorer());

        var rows = ReadAll(result);
        Assert.Equal(4, rows.Count);
        // Sorted by |val|: |-1|=1, |2|=2, |-3|=3, |10|=10
        Assert.Equal(-1, rows[0][0].AsInt64());
        Assert.Equal(2, rows[1][0].AsInt64());
        Assert.Equal(-3, rows[2][0].AsInt64());
        Assert.Equal(10, rows[3][0].AsInt64());
    }

    [Fact]
    public void Apply_KGreaterThanRows_ReturnsAll()
    {
        var reader = MakeIntReader(5, 3, 1);
        var result = ScoredTopKProcessor.Apply(reader, 50, new AbsScorer());

        var rows = ReadAll(result);
        Assert.Equal(3, rows.Count);
        // Sorted ascending by |val|: 1, 3, 5
        Assert.Equal(1, rows[0][0].AsInt64());
        Assert.Equal(3, rows[1][0].AsInt64());
        Assert.Equal(5, rows[2][0].AsInt64());
    }

    [Fact]
    public void Apply_KEqualsOne_ReturnsBest()
    {
        var reader = MakeReader(
            (10, 10),
            (1, 0),   // closest to origin
            (5, 5));

        var result = ScoredTopKProcessor.Apply(reader, 1, new DistanceScorer(0, 0));

        var rows = ReadAll(result);
        Assert.Single(rows);
        Assert.Equal(1.0, rows[0][0].AsDouble());
        Assert.Equal(0.0, rows[0][1].AsDouble());
    }

    [Fact]
    public void Apply_EmptySource_ReturnsEmpty()
    {
        var reader = new SharcDataReader(Array.Empty<QueryValue[]>(), ["x", "y"]);
        var result = ScoredTopKProcessor.Apply(reader, 10, new DistanceScorer(0, 0));

        var rows = ReadAll(result);
        Assert.Empty(rows);
    }

    [Fact]
    public void Apply_AllSameScore_ReturnsK()
    {
        // All at distance 5 from origin
        var reader = MakeReader((3, 4), (0, 5), (5, 0), (4, 3));
        var result = ScoredTopKProcessor.Apply(reader, 2, new DistanceScorer(0, 0));

        var rows = ReadAll(result);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Apply_LambdaOverload_Works()
    {
        var reader = MakeIntReader(30, 10, 20, 5);
        var result = ScoredTopKProcessor.Apply(reader, 2,
            row => (double)row.GetInt64(0));

        var rows = ReadAll(result);
        Assert.Equal(2, rows.Count);
        // Two lowest: 5, 10
        Assert.Equal(5, rows[0][0].AsInt64());
        Assert.Equal(10, rows[1][0].AsInt64());
    }

    [Fact]
    public void Apply_PreservesAllColumns()
    {
        // Ensure all columns are materialized, not just scored ones
        var rows = new QueryValue[3][];
        rows[0] = [QueryValue.FromDouble(5), QueryValue.FromDouble(5), QueryValue.FromString("far")];
        rows[1] = [QueryValue.FromDouble(1), QueryValue.FromDouble(0), QueryValue.FromString("near")];
        rows[2] = [QueryValue.FromDouble(3), QueryValue.FromDouble(3), QueryValue.FromString("mid")];
        var reader = new SharcDataReader(rows, ["x", "y", "label"]);

        var result = ScoredTopKProcessor.Apply(reader, 2, new DistanceScorer(0, 0));

        var output = ReadAll(result);
        Assert.Equal(2, output.Count);
        Assert.Equal("near", output[0][2].AsString());
        Assert.Equal("mid", output[1][2].AsString());
    }

    [Fact]
    public void Apply_LargeDataset_ReturnsCorrectTopK()
    {
        // 100 points, find 5 nearest to origin
        var points = new (double x, double y)[100];
        for (int i = 0; i < 100; i++)
            points[i] = (i, i); // distances: 0, sqrt(2), 2*sqrt(2), ...

        var reader = MakeReader(points);
        var result = ScoredTopKProcessor.Apply(reader, 5, new DistanceScorer(0, 0));

        var output = ReadAll(result);
        Assert.Equal(5, output.Count);

        // Should be (0,0), (1,1), (2,2), (3,3), (4,4)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((double)i, output[i][0].AsDouble());
            Assert.Equal((double)i, output[i][1].AsDouble());
        }
    }

    // ── Helper ──────────────────────────────────────────────────────

    private static List<QueryValue[]> ReadAll(SharcDataReader reader)
    {
        var list = new List<QueryValue[]>();
        while (reader.Read())
        {
            var row = new QueryValue[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.GetColumnType(i) switch
                {
                    SharcColumnType.Integral => QueryValue.FromInt64(reader.GetInt64(i)),
                    SharcColumnType.Real => QueryValue.FromDouble(reader.GetDouble(i)),
                    SharcColumnType.Text => QueryValue.FromString(reader.GetString(i)),
                    SharcColumnType.Blob => QueryValue.FromBlob(reader.GetBlob(i)),
                    _ => QueryValue.Null,
                };
            }
            list.Add(row);
        }
        reader.Dispose();
        return list;
    }
}
