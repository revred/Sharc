// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Sharc.Graph.Query;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Query;

public sealed class GraphTraversalCursorTests
{
    private static TraversalNode MakeNode(long key, int typeId, string? alias, string data, int tokens, int depth)
    {
        var record = new GraphRecord(
            new RecordId("test", $"id-{key}", new NodeKey(key)),
            new NodeKey(key), typeId, data, alias: alias)
        {
            Tokens = tokens
        };
        return new TraversalNode(record, depth, null);
    }

    [Fact]
    public void Cursor_EmptyResult_MoveNextReturnsFalse()
    {
        using var cursor = new GraphTraversalCursor(Array.Empty<TraversalNode>());
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Cursor_SingleNode_ReturnsCorrectValues()
    {
        var nodes = new[] { MakeNode(100, 5, "myAlias", "{\"x\":1}", 42, 0) };
        using var cursor = new GraphTraversalCursor(nodes);

        Assert.True(cursor.MoveNext());
        Assert.Equal(100L, cursor.GetInt64(0)); // key
        Assert.Equal(5L, cursor.GetInt64(1));   // kind (typeId)
        Assert.Equal("myAlias", cursor.GetString(2)); // alias
        Assert.Equal("{\"x\":1}", cursor.GetString(3)); // data
        Assert.Equal(42L, cursor.GetInt64(4)); // tokens
        Assert.Equal(0L, cursor.GetInt64(5));  // depth
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Cursor_KeyOrdinal_ReturnsNodeKey()
    {
        var nodes = new[] { MakeNode(999, 1, null, "{}", 0, 3) };
        using var cursor = new GraphTraversalCursor(nodes);

        Assert.True(cursor.MoveNext());
        Assert.Equal(999L, cursor.GetInt64(0));
    }

    [Fact]
    public void Cursor_AliasOrdinal_NullReturnsEmpty()
    {
        var nodes = new[] { MakeNode(100, 1, null, "{}", 0, 0) };
        using var cursor = new GraphTraversalCursor(nodes);

        Assert.True(cursor.MoveNext());
        Assert.True(cursor.IsNull(2)); // alias is null
    }

    [Fact]
    public void Cursor_ColumnNames_MatchSchema()
    {
        using var cursor = new GraphTraversalCursor(Array.Empty<TraversalNode>());

        Assert.Equal("key", cursor.GetColumnName(0));
        Assert.Equal("kind", cursor.GetColumnName(1));
        Assert.Equal("alias", cursor.GetColumnName(2));
        Assert.Equal("data", cursor.GetColumnName(3));
        Assert.Equal("tokens", cursor.GetColumnName(4));
        Assert.Equal("depth", cursor.GetColumnName(5));
    }

    [Fact]
    public void Cursor_FieldCount_IsSix()
    {
        using var cursor = new GraphTraversalCursor(Array.Empty<TraversalNode>());
        Assert.Equal(6, cursor.FieldCount);
    }

    [Fact]
    public void Cursor_ColumnTypes_MatchSchema()
    {
        var nodes = new[] { MakeNode(1, 1, "a", "{}", 0, 0) };
        using var cursor = new GraphTraversalCursor(nodes);
        Assert.True(cursor.MoveNext());

        Assert.Equal(SharcColumnType.Integral, cursor.GetColumnType(0));
        Assert.Equal(SharcColumnType.Integral, cursor.GetColumnType(1));
        Assert.Equal(SharcColumnType.Text, cursor.GetColumnType(2));
        Assert.Equal(SharcColumnType.Text, cursor.GetColumnType(3));
        Assert.Equal(SharcColumnType.Integral, cursor.GetColumnType(4));
        Assert.Equal(SharcColumnType.Integral, cursor.GetColumnType(5));
    }

    [Fact]
    public void Cursor_MultipleNodes_RowsReadIncrementsCorrectly()
    {
        var nodes = new[]
        {
            MakeNode(1, 1, null, "{}", 0, 0),
            MakeNode(2, 1, null, "{}", 0, 1),
            MakeNode(3, 1, null, "{}", 0, 2)
        };
        using var cursor = new GraphTraversalCursor(nodes);

        Assert.True(cursor.MoveNext());
        Assert.Equal(1, cursor.RowsRead);
        Assert.True(cursor.MoveNext());
        Assert.Equal(2, cursor.RowsRead);
        Assert.True(cursor.MoveNext());
        Assert.Equal(3, cursor.RowsRead);
        Assert.False(cursor.MoveNext());
    }
}
