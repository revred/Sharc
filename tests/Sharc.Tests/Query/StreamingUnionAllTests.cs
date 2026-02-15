// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class StreamingUnionAllTests
{
    private static QueryValue[] Row(long id, string name) =>
        [QueryValue.FromInt64(id), QueryValue.FromString(name)];

    private static SharcDataReader MakeReader(QueryValue[][] rows, string[] columns) =>
        new(rows, columns);

    // ─── Basic concatenation ────────────────────────────────────

    [Fact]
    public void ConcatReader_ReturnsAllRowsFromBothReaders()
    {
        var left = MakeReader(
            [Row(1, "Alice"), Row(2, "Bob")],
            ["id", "name"]);
        var right = MakeReader(
            [Row(3, "Carol"), Row(4, "Dave")],
            ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        var names = new List<string>();
        while (concat.Read())
            names.Add(concat.GetString(1));

        Assert.Equal(4, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
        Assert.Equal("Carol", names[2]);
        Assert.Equal("Dave", names[3]);
    }

    [Fact]
    public void ConcatReader_FieldCount_MatchesColumnNames()
    {
        var left = MakeReader([Row(1, "A")], ["id", "name"]);
        var right = MakeReader([Row(2, "B")], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);
        Assert.Equal(2, concat.FieldCount);
    }

    // ─── Typed accessors across boundary ────────────────────────

    [Fact]
    public void ConcatReader_GetInt64_WorksAcrossBoundary()
    {
        var left = MakeReader([Row(10, "X")], ["id", "name"]);
        var right = MakeReader([Row(20, "Y")], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        Assert.True(concat.Read());
        Assert.Equal(10L, concat.GetInt64(0));

        Assert.True(concat.Read());
        Assert.Equal(20L, concat.GetInt64(0));

        Assert.False(concat.Read());
    }

    [Fact]
    public void ConcatReader_GetDouble_WorksAcrossBoundary()
    {
        var leftRows = new[] { new[] { QueryValue.FromDouble(1.5), QueryValue.FromString("a") } };
        var rightRows = new[] { new[] { QueryValue.FromDouble(2.5), QueryValue.FromString("b") } };

        var left = MakeReader(leftRows, ["val", "label"]);
        var right = MakeReader(rightRows, ["val", "label"]);

        using var concat = new SharcDataReader(left, right, ["val", "label"]);

        Assert.True(concat.Read());
        Assert.Equal(1.5, concat.GetDouble(0));

        Assert.True(concat.Read());
        Assert.Equal(2.5, concat.GetDouble(0));
    }

    [Fact]
    public void ConcatReader_GetColumnType_WorksAcrossBoundary()
    {
        var left = MakeReader([Row(1, "X")], ["id", "name"]);
        var right = MakeReader([Row(2, "Y")], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        Assert.True(concat.Read());
        Assert.Equal(SharcColumnType.Integral, concat.GetColumnType(0));
        Assert.Equal(SharcColumnType.Text, concat.GetColumnType(1));

        Assert.True(concat.Read());
        Assert.Equal(SharcColumnType.Integral, concat.GetColumnType(0));
        Assert.Equal(SharcColumnType.Text, concat.GetColumnType(1));
    }

    // ─── Edge cases ─────────────────────────────────────────────

    [Fact]
    public void ConcatReader_EmptyLeft_ReturnsOnlyRight()
    {
        var left = MakeReader([], ["id", "name"]);
        var right = MakeReader([Row(1, "Solo")], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        var names = new List<string>();
        while (concat.Read())
            names.Add(concat.GetString(1));

        Assert.Single(names);
        Assert.Equal("Solo", names[0]);
    }

    [Fact]
    public void ConcatReader_EmptyRight_ReturnsOnlyLeft()
    {
        var left = MakeReader([Row(1, "Solo")], ["id", "name"]);
        var right = MakeReader([], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        var names = new List<string>();
        while (concat.Read())
            names.Add(concat.GetString(1));

        Assert.Single(names);
        Assert.Equal("Solo", names[0]);
    }

    [Fact]
    public void ConcatReader_BothEmpty_ReturnsNothing()
    {
        var left = MakeReader([], ["id"]);
        var right = MakeReader([], ["id"]);

        using var concat = new SharcDataReader(left, right, ["id"]);
        Assert.False(concat.Read());
    }

    // ─── Null handling ──────────────────────────────────────────

    [Fact]
    public void ConcatReader_NullValues_HandledCorrectly()
    {
        var leftRows = new[] { new[] { QueryValue.FromInt64(1), QueryValue.Null } };
        var rightRows = new[] { new[] { QueryValue.Null, QueryValue.FromString("text") } };

        var left = MakeReader(leftRows, ["id", "val"]);
        var right = MakeReader(rightRows, ["id", "val"]);

        using var concat = new SharcDataReader(left, right, ["id", "val"]);

        Assert.True(concat.Read());
        Assert.False(concat.IsNull(0));
        Assert.True(concat.IsNull(1));

        Assert.True(concat.Read());
        Assert.True(concat.IsNull(0));
        Assert.False(concat.IsNull(1));
    }

    // ─── GetValue (boxing boundary) ─────────────────────────────

    [Fact]
    public void ConcatReader_GetValue_BoxesCorrectly()
    {
        var left = MakeReader([Row(42, "hello")], ["id", "name"]);
        var right = MakeReader([Row(99, "world")], ["id", "name"]);

        using var concat = new SharcDataReader(left, right, ["id", "name"]);

        Assert.True(concat.Read());
        Assert.Equal(42L, concat.GetValue(0));
        Assert.Equal("hello", concat.GetValue(1));

        Assert.True(concat.Read());
        Assert.Equal(99L, concat.GetValue(0));
        Assert.Equal("world", concat.GetValue(1));
    }
}
