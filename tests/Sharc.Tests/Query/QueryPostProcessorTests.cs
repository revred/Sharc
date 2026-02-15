// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Tests.Query;

public class QueryPostProcessorTests
{
    private static QueryValue[] Row(long a) => [QueryValue.FromInt64(a)];
    private static QueryValue[] Row(long a, string b) => [QueryValue.FromInt64(a), QueryValue.FromString(b)];
    private static QueryValue[] Row(string a) => [QueryValue.FromString(a)];

    // ─── ApplyOrderBy ──────────────────────────────────────────

    [Fact]
    public void ApplyOrderBy_SingleColumnAscending_SortsCorrectly()
    {
        var rows = new List<QueryValue[]> { Row(3), Row(1), Row(2) };
        var orderBy = new List<OrderIntent> { new() { ColumnName = "id", Descending = false } };
        var columns = new[] { "id" };

        QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns);

        Assert.Equal(1L, rows[0][0].AsInt64());
        Assert.Equal(2L, rows[1][0].AsInt64());
        Assert.Equal(3L, rows[2][0].AsInt64());
    }

    [Fact]
    public void ApplyOrderBy_SingleColumnDescending_SortsReversed()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(3), Row(2) };
        var orderBy = new List<OrderIntent> { new() { ColumnName = "id", Descending = true } };
        var columns = new[] { "id" };

        QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns);

        Assert.Equal(3L, rows[0][0].AsInt64());
        Assert.Equal(2L, rows[1][0].AsInt64());
        Assert.Equal(1L, rows[2][0].AsInt64());
    }

    [Fact]
    public void ApplyOrderBy_MultiColumn_SortsByPrimaryThenSecondary()
    {
        var rows = new List<QueryValue[]>
        {
            Row(1, "b"), Row(1, "a"), Row(2, "c"), Row(2, "a")
        };
        var orderBy = new List<OrderIntent>
        {
            new() { ColumnName = "id", Descending = false },
            new() { ColumnName = "name", Descending = false }
        };
        var columns = new[] { "id", "name" };

        QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns);

        Assert.Equal("a", rows[0][1].AsString()); // (1, "a")
        Assert.Equal("b", rows[1][1].AsString()); // (1, "b")
        Assert.Equal("a", rows[2][1].AsString()); // (2, "a")
        Assert.Equal("c", rows[3][1].AsString()); // (2, "c")
    }

    [Fact]
    public void ApplyOrderBy_EmptyRows_NoError()
    {
        var rows = new List<QueryValue[]>();
        var orderBy = new List<OrderIntent> { new() { ColumnName = "id", Descending = false } };
        var columns = new[] { "id" };

        QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns);

        Assert.Empty(rows);
    }

    [Fact]
    public void ApplyOrderBy_NullValues_SortLast()
    {
        var rows = new List<QueryValue[]>
        {
            new[] { QueryValue.Null }, Row(1), new[] { QueryValue.Null }, Row(2)
        };
        var orderBy = new List<OrderIntent> { new() { ColumnName = "id", Descending = false } };
        var columns = new[] { "id" };

        QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns);

        Assert.Equal(1L, rows[0][0].AsInt64());
        Assert.Equal(2L, rows[1][0].AsInt64());
        Assert.True(rows[2][0].IsNull);
        Assert.True(rows[3][0].IsNull);
    }

    [Fact]
    public void ApplyOrderBy_UnknownColumn_ThrowsArgumentException()
    {
        var rows = new List<QueryValue[]> { Row(1) };
        var orderBy = new List<OrderIntent> { new() { ColumnName = "nonexistent", Descending = false } };
        var columns = new[] { "id" };

        Assert.Throws<ArgumentException>(() =>
            QueryPostProcessor.ApplyOrderBy(rows, orderBy, columns));
    }

    // ─── ApplyLimitOffset ──────────────────────────────────────

    [Fact]
    public void ApplyLimitOffset_LimitOnly_TruncatesRows()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2), Row(3), Row(4), Row(5) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, 3, null);
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0][0].AsInt64());
        Assert.Equal(3L, result[2][0].AsInt64());
    }

    [Fact]
    public void ApplyLimitOffset_OffsetOnly_SkipsRows()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2), Row(3), Row(4), Row(5) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, null, 2);
        Assert.Equal(3, result.Count);
        Assert.Equal(3L, result[0][0].AsInt64());
    }

    [Fact]
    public void ApplyLimitOffset_BothLimitAndOffset_Slices()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2), Row(3), Row(4), Row(5) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, 2, 1);
        Assert.Equal(2, result.Count);
        Assert.Equal(2L, result[0][0].AsInt64());
        Assert.Equal(3L, result[1][0].AsInt64());
    }

    [Fact]
    public void ApplyLimitOffset_LimitExceedsRows_ReturnsAll()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, 100, null);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyLimitOffset_OffsetExceedsRows_ReturnsEmpty()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, null, 100);
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyLimitOffset_ZeroLimit_ReturnsEmpty()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2), Row(3) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, 0, null);
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyLimitOffset_NoLimitNoOffset_ReturnsSameList()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2) };
        var result = QueryPostProcessor.ApplyLimitOffset(rows, null, null);
        Assert.Same(rows, result); // same reference — no copy
    }

    [Fact]
    public void ApplyLimitOffset_EmptyRows_ReturnsEmpty()
    {
        var rows = new List<QueryValue[]>();
        var result = QueryPostProcessor.ApplyLimitOffset(rows, 10, 5);
        Assert.Empty(result);
    }

    // ─── ResolveOrdinal ──────────────────────────────────────────

    [Fact]
    public void ResolveOrdinal_ExistingColumn_ReturnsIndex()
    {
        var columns = new[] { "id", "name", "age" };
        Assert.Equal(1, QueryPostProcessor.ResolveOrdinal(columns, "name"));
    }

    [Fact]
    public void ResolveOrdinal_CaseInsensitive_Matches()
    {
        var columns = new[] { "id", "Name", "age" };
        Assert.Equal(1, QueryPostProcessor.ResolveOrdinal(columns, "name"));
        Assert.Equal(1, QueryPostProcessor.ResolveOrdinal(columns, "NAME"));
    }

    [Fact]
    public void ResolveOrdinal_NotFound_ThrowsArgumentException()
    {
        var columns = new[] { "id", "name" };
        Assert.Throws<ArgumentException>(() =>
            QueryPostProcessor.ResolveOrdinal(columns, "nonexistent"));
    }

    // ─── CompareValues ──────────────────────────────────────────

    [Fact]
    public void CompareValues_TwoIntegers_Compares()
    {
        var a = QueryValue.FromInt64(5);
        var b = QueryValue.FromInt64(3);
        Assert.True(QueryPostProcessor.CompareValues(a, b) > 0);
    }

    [Fact]
    public void CompareValues_IntAndDouble_CrossTypeComparison()
    {
        var intVal = QueryValue.FromInt64(5);
        var dblVal = QueryValue.FromDouble(5.0);
        Assert.Equal(0, QueryPostProcessor.CompareValues(intVal, dblVal));
    }

    [Fact]
    public void CompareValues_NullSortsLast()
    {
        var val = QueryValue.FromInt64(1);
        var nul = QueryValue.Null;
        Assert.True(QueryPostProcessor.CompareValues(val, nul) < 0); // val before null
        Assert.True(QueryPostProcessor.CompareValues(nul, val) > 0); // null after val
    }

    [Fact]
    public void CompareValues_BothNull_Equal()
    {
        Assert.Equal(0, QueryPostProcessor.CompareValues(QueryValue.Null, QueryValue.Null));
    }

    [Fact]
    public void CompareValues_Strings_OrdinalComparison()
    {
        var a = QueryValue.FromString("apple");
        var b = QueryValue.FromString("banana");
        Assert.True(QueryPostProcessor.CompareValues(a, b) < 0);
    }
}
