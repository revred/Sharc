// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class TopKIntegrationTests
{
    /// <summary>Euclidean distance from a target point.</summary>
    private sealed class DistanceScorer(double cx, double cy) : IRowScorer
    {
        public double Score(IRowAccessor row) =>
            Math.Sqrt(Math.Pow(row.GetDouble(0) - cx, 2) + Math.Pow(row.GetDouble(1) - cy, 2));
    }

    [Fact]
    public void TopK_WithDatabase_EndToEnd()
    {
        // Create table with x, y columns and 100 points
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE points (id INTEGER PRIMARY KEY, x REAL, y REAL)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 100; i++)
            {
                cmd.CommandText = $"INSERT INTO points (x, y) VALUES ({i}.0, {i}.0)";
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("points");

        // Find 5 nearest to origin — project x, y
        using var reader = jit.TopK(5, new DistanceScorer(0, 0), "x", "y");

        var results = new List<(double x, double y)>();
        while (reader.Read())
            results.Add((reader.GetDouble(0), reader.GetDouble(1)));

        Assert.Equal(5, results.Count);

        // Should be (0,0), (1,1), (2,2), (3,3), (4,4) sorted by distance
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((double)i, results[i].x);
            Assert.Equal((double)i, results[i].y);
        }
    }

    [Fact]
    public void TopK_WithFilterChain_FiltersFirst()
    {
        // Create table with 100 points, filter to x >= 10, then TopK
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE points (id INTEGER PRIMARY KEY, x REAL, y REAL)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 100; i++)
            {
                cmd.CommandText = $"INSERT INTO points (x, y) VALUES ({i}.0, {i}.0)";
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("points");

        // Filter: only x >= 10, then find 3 nearest to (10, 10)
        jit.Where(FilterStar.Column("x").Gte(10.0));
        using var reader = jit.TopK(3, new DistanceScorer(10, 10), "x", "y");

        var results = new List<(double x, double y)>();
        while (reader.Read())
            results.Add((reader.GetDouble(0), reader.GetDouble(1)));

        Assert.Equal(3, results.Count);

        // (10,10) is distance 0, (11,11) is ~1.41, (12,12) is ~2.83
        Assert.Equal(10.0, results[0].x);
        Assert.Equal(10.0, results[0].y);
        Assert.Equal(11.0, results[1].x);
        Assert.Equal(12.0, results[2].x);
    }

    [Fact]
    public void TopK_WithIndexedFilter_UsesIndexAndReturnsNearest()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE points (id INTEGER PRIMARY KEY, x REAL, y REAL, payload TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX idx_points_x ON points (x)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 200; i++)
            {
                cmd.CommandText = "INSERT INTO points (x, y, payload) VALUES ($x, $y, $p)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$x", (double)i);
                cmd.Parameters.AddWithValue("$y", (double)i);
                cmd.Parameters.AddWithValue("$p", new string('Z', 256));
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("points");
        jit.Where(FilterStar.Column("x").Between(50.0, 80.0));

        using var reader = jit.TopK(4, new DistanceScorer(50, 50), "id", "x", "y", "payload");

        // P1.4 path is cursor-backed and should be index-accelerated before TopK materialization.
        Assert.True(reader.Read());
        var rows = new List<(long id, double x, double y)>();
        do
        {
            rows.Add((reader.GetInt64(0), reader.GetDouble(1), reader.GetDouble(2)));
            Assert.Equal(256, reader.GetString(3).Length);
        }
        while (reader.Read());

        Assert.Equal(4, rows.Count);
        Assert.Equal(50.0, rows[0].x);
        Assert.Equal(51.0, rows[1].x);
        Assert.Equal(52.0, rows[2].x);
        Assert.Equal(53.0, rows[3].x);
    }

    [Fact]
    public void TopK_OnWithoutRowIdTable_FallsBackToEagerMaterialization()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE points_wr (key TEXT PRIMARY KEY, x REAL, y REAL) WITHOUT ROWID";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 20; i++)
            {
                cmd.CommandText = "INSERT INTO points_wr (key, x, y) VALUES ($k, $x, $y)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$k", $"p{i:D2}");
                cmd.Parameters.AddWithValue("$x", (double)i);
                cmd.Parameters.AddWithValue("$y", (double)i);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("points_wr");

        using var reader = jit.TopK(3, new DistanceScorer(10, 10), "x", "y");

        var results = new List<(double x, double y)>();
        while (reader.Read())
            results.Add((reader.GetDouble(0), reader.GetDouble(1)));

        Assert.Equal(3, results.Count);
        Assert.Equal(10.0, results[0].x);
        Assert.Equal(9.0, results[1].x);
        Assert.Equal(11.0, results[2].x);
    }

    [Fact]
    public void TopK_LambdaOverload_WithDatabase()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE points (id INTEGER PRIMARY KEY, x REAL, y REAL)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 20; i++)
            {
                cmd.CommandText = $"INSERT INTO points (x, y) VALUES ({i}.0, {i}.0)";
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("points");

        // Lambda scorer: distance from (5, 5)
        using var reader = jit.TopK(3,
            row => Math.Sqrt(Math.Pow(row.GetDouble(0) - 5, 2) + Math.Pow(row.GetDouble(1) - 5, 2)),
            "x", "y");

        var results = new List<(double x, double y)>();
        while (reader.Read())
            results.Add((reader.GetDouble(0), reader.GetDouble(1)));

        Assert.Equal(3, results.Count);
        // Nearest to (5,5): (5,5) at d=0, (4,4) at d=~1.41, (6,6) at d=~1.41
        Assert.Equal(5.0, results[0].x);
    }

    [Fact]
    public void TopK_KZero_Throws()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, val REAL)";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("t");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            jit.TopK(0, new DistanceScorer(0, 0), "val"));
    }

    [Fact]
    public void TopK_NullScorer_Throws()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, val REAL)";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("t");

        Assert.Throws<ArgumentNullException>(() =>
            jit.TopK(1, (IRowScorer)null!, "val"));
    }
}
