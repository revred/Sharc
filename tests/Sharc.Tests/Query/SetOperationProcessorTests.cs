// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Tests.Query;

public class SetOperationProcessorTests
{
    private static QueryValue[] Row(long a) => [QueryValue.FromInt64(a)];
    private static QueryValue[] Row(long a, string b) => [QueryValue.FromInt64(a), QueryValue.FromString(b)];

    // ─── ApplyDistinct ──────────────────────────────────────────

    [Fact]
    public void ApplyDistinct_NoDuplicates_ReturnsAll()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(2), Row(3) };
        var result = SetOperationProcessor.ApplyDistinct(rows, 1);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ApplyDistinct_AllDuplicates_ReturnsSingle()
    {
        var rows = new List<QueryValue[]> { Row(1), Row(1), Row(1) };
        var result = SetOperationProcessor.ApplyDistinct(rows, 1);
        Assert.Single(result);
        Assert.Equal(1L, result[0][0].AsInt64());
    }

    [Fact]
    public void ApplyDistinct_PreservesFirstOccurrenceOrder()
    {
        var rows = new List<QueryValue[]> { Row(3), Row(1), Row(3), Row(2), Row(1) };
        var result = SetOperationProcessor.ApplyDistinct(rows, 1);
        Assert.Equal(3, result.Count);
        Assert.Equal(3L, result[0][0].AsInt64());
        Assert.Equal(1L, result[1][0].AsInt64());
        Assert.Equal(2L, result[2][0].AsInt64());
    }

    [Fact]
    public void ApplyDistinct_EmptyInput_ReturnsEmpty()
    {
        var rows = new List<QueryValue[]>();
        var result = SetOperationProcessor.ApplyDistinct(rows, 1);
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyDistinct_MultiColumn_ComparesAllColumns()
    {
        var rows = new List<QueryValue[]>
        {
            Row(1, "a"), Row(1, "b"), Row(1, "a"), Row(2, "a")
        };
        var result = SetOperationProcessor.ApplyDistinct(rows, 2);
        Assert.Equal(3, result.Count); // (1,a), (1,b), (2,a)
    }

    [Fact]
    public void ApplyDistinct_NullValues_TreatsNullsAsEqual()
    {
        var nullRow1 = new QueryValue[] { QueryValue.Null };
        var nullRow2 = new QueryValue[] { QueryValue.Null };
        var rows = new List<QueryValue[]> { nullRow1, Row(1), nullRow2 };
        var result = SetOperationProcessor.ApplyDistinct(rows, 1);
        Assert.Equal(2, result.Count); // NULL and 1
    }

    // ─── UnionAll ──────────────────────────────────────────────

    [Fact]
    public void Apply_UnionAll_ConcatenatesWithoutDedup()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(2), Row(3) };
        var result = SetOperationProcessor.Apply(CompoundOperator.UnionAll, left, right, 1);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Apply_UnionAll_EmptyLeft_ReturnsRight()
    {
        var left = new List<QueryValue[]>();
        var right = new List<QueryValue[]> { Row(1), Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.UnionAll, left, right, 1);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_UnionAll_EmptyRight_ReturnsLeft()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]>();
        var result = SetOperationProcessor.Apply(CompoundOperator.UnionAll, left, right, 1);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_UnionAll_BothEmpty_ReturnsEmpty()
    {
        var left = new List<QueryValue[]>();
        var right = new List<QueryValue[]>();
        var result = SetOperationProcessor.Apply(CompoundOperator.UnionAll, left, right, 1);
        Assert.Empty(result);
    }

    // ─── Union (deduplicated) ──────────────────────────────────

    [Fact]
    public void Apply_Union_RemovesDuplicatesAcrossSides()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(2), Row(3) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 1);
        Assert.Equal(3, result.Count); // 1, 2, 3
    }

    [Fact]
    public void Apply_Union_RemovesDuplicatesWithinSameSide()
    {
        var left = new List<QueryValue[]> { Row(1), Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(3) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 1);
        Assert.Equal(3, result.Count); // 1, 2, 3
    }

    [Fact]
    public void Apply_Union_AllIdentical_ReturnsSingle()
    {
        var left = new List<QueryValue[]> { Row(1), Row(1) };
        var right = new List<QueryValue[]> { Row(1), Row(1) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 1);
        Assert.Single(result);
    }

    [Fact]
    public void Apply_Union_EmptyInputs_ReturnsEmpty()
    {
        var left = new List<QueryValue[]>();
        var right = new List<QueryValue[]>();
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 1);
        Assert.Empty(result);
    }

    // ─── Intersect ──────────────────────────────────────────────

    [Fact]
    public void Apply_Intersect_ReturnsOnlyCommonRows()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2), Row(3) };
        var right = new List<QueryValue[]> { Row(2), Row(3), Row(4) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Equal(2, result.Count);
        Assert.Equal(2L, result[0][0].AsInt64());
        Assert.Equal(3L, result[1][0].AsInt64());
    }

    [Fact]
    public void Apply_Intersect_NoOverlap_ReturnsEmpty()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(3), Row(4) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_Intersect_DuplicatesInLeft_ReturnsOnce()
    {
        var left = new List<QueryValue[]> { Row(1), Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(1) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Single(result);
        Assert.Equal(1L, result[0][0].AsInt64());
    }

    [Fact]
    public void Apply_Intersect_EmptyLeft_ReturnsEmpty()
    {
        var left = new List<QueryValue[]>();
        var right = new List<QueryValue[]> { Row(1), Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_Intersect_EmptyRight_ReturnsEmpty()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]>();
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Empty(result);
    }

    // ─── Except ──────────────────────────────────────────────

    [Fact]
    public void Apply_Except_RemovesRightFromLeft()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2), Row(3) };
        var right = new List<QueryValue[]> { Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Except, left, right, 1);
        Assert.Equal(2, result.Count);
        Assert.Equal(1L, result[0][0].AsInt64());
        Assert.Equal(3L, result[1][0].AsInt64());
    }

    [Fact]
    public void Apply_Except_NoOverlap_ReturnsAllLeft()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(3), Row(4) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Except, left, right, 1);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_Except_AllExcluded_ReturnsEmpty()
    {
        var left = new List<QueryValue[]> { Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(1), Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Except, left, right, 1);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_Except_DuplicatesInLeft_DedupedInResult()
    {
        var left = new List<QueryValue[]> { Row(1), Row(1), Row(2) };
        var right = new List<QueryValue[]> { Row(3) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Except, left, right, 1);
        Assert.Equal(2, result.Count); // 1, 2 (deduped)
    }

    [Fact]
    public void Apply_Except_EmptyRight_ReturnsLeftDeduped()
    {
        var left = new List<QueryValue[]> { Row(1), Row(1), Row(2) };
        var right = new List<QueryValue[]>();
        var result = SetOperationProcessor.Apply(CompoundOperator.Except, left, right, 1);
        Assert.Equal(2, result.Count); // 1, 2
    }

    // ─── MultiColumn ──────────────────────────────────────────

    [Fact]
    public void Apply_Union_MultiColumn_ComparesAllColumns()
    {
        var left = new List<QueryValue[]> { Row(1, "a"), Row(1, "b") };
        var right = new List<QueryValue[]> { Row(1, "a"), Row(2, "a") };
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 2);
        Assert.Equal(3, result.Count); // (1,a), (1,b), (2,a)
    }

    [Fact]
    public void Apply_Intersect_MultiColumn_MatchesOnAllColumns()
    {
        var left = new List<QueryValue[]> { Row(1, "a"), Row(1, "b") };
        var right = new List<QueryValue[]> { Row(1, "a"), Row(2, "a") };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 2);
        Assert.Single(result); // only (1, "a") matches
        Assert.Equal(1L, result[0][0].AsInt64());
        Assert.Equal("a", result[0][1].AsString());
    }

    // ─── Null handling in set operations ──────────────────────

    [Fact]
    public void Apply_Union_NullRows_TreatsNullsAsEqual()
    {
        var nullRow = new QueryValue[] { QueryValue.Null };
        var left = new List<QueryValue[]> { nullRow, Row(1) };
        var right = new List<QueryValue[]> { new[] { QueryValue.Null }, Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Union, left, right, 1);
        Assert.Equal(3, result.Count); // NULL, 1, 2
    }

    [Fact]
    public void Apply_Intersect_NullInBothSides_ReturnsNull()
    {
        var left = new List<QueryValue[]> { new[] { QueryValue.Null }, Row(1) };
        var right = new List<QueryValue[]> { new[] { QueryValue.Null }, Row(2) };
        var result = SetOperationProcessor.Apply(CompoundOperator.Intersect, left, right, 1);
        Assert.Single(result);
        Assert.True(result[0][0].IsNull);
    }
}
