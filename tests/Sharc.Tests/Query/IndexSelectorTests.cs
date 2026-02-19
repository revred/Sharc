// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Sharc.Query.Intent;
using Sharc.Query.Optimization;
using Xunit;

namespace Sharc.Tests.Query;

public class IndexSelectorTests
{
    private static IndexInfo MakeIndex(string name, bool isUnique, params string[] columns)
    {
        var cols = new List<IndexColumnInfo>();
        for (int i = 0; i < columns.Length; i++)
            cols.Add(new IndexColumnInfo { Name = columns[i], Ordinal = i, IsDescending = false });

        return new IndexInfo
        {
            Name = name,
            TableName = "test_table",
            RootPage = 10 + Math.Abs(name.GetHashCode() % 100),
            Sql = $"CREATE INDEX {name} ON test_table({string.Join(", ", columns)})",
            IsUnique = isUnique,
            Columns = cols
        };
    }

    [Fact]
    public void SelectBestIndex_EqOnIndexedColumn_ReturnsPlan()
    {
        var indexes = new[] { MakeIndex("idx_user_id", false, "user_id") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.Equal("idx_user_id", plan.Index!.Name);
        Assert.Equal(42L, plan.SeekKey);
        Assert.Equal(IntentOp.Eq, plan.SeekOp);
        Assert.Equal("user_id", plan.ConsumedColumn);
    }

    [Fact]
    public void SelectBestIndex_NoMatchingIndex_ReturnsEmptyPlan()
    {
        var indexes = new[] { MakeIndex("idx_age", false, "age") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.Null(plan.Index);
    }

    [Fact]
    public void SelectBestIndex_PrefersUniqueOverNonUnique()
    {
        var indexes = new[]
        {
            MakeIndex("idx_user_id", false, "user_id"),
            MakeIndex("idx_user_id_unique", true, "user_id")
        };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.True(plan.Index!.IsUnique);
        Assert.Equal("idx_user_id_unique", plan.Index.Name);
    }

    [Fact]
    public void SelectBestIndex_PrefersEqOverRange()
    {
        var indexes = new[]
        {
            MakeIndex("idx_age", false, "age"),
            MakeIndex("idx_user_id", false, "user_id")
        };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "age", Op = IntentOp.Gt, IntegerValue = 25, IsIntegerKey = true },
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.Equal("idx_user_id", plan.Index!.Name);
        Assert.Equal(IntentOp.Eq, plan.SeekOp);
    }

    [Fact]
    public void SelectBestIndex_MultiColumnIndex_MatchesFirstColumnOnly()
    {
        // Index on (user_id, created_at) — only first column used for seek
        var indexes = new[] { MakeIndex("idx_composite", false, "user_id", "created_at") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.Equal("idx_composite", plan.Index!.Name);
    }

    [Fact]
    public void SelectBestIndex_MultiColumnIndex_SecondColumnNotMatched()
    {
        // Index on (user_id, created_at) — seeking on created_at alone can't use this index
        var indexes = new[] { MakeIndex("idx_composite", false, "user_id", "created_at") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "created_at", Op = IntentOp.Eq, IntegerValue = 100, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.Null(plan.Index);
    }

    [Fact]
    public void SelectBestIndex_BetweenCondition_ReturnsPlanWithBounds()
    {
        var indexes = new[] { MakeIndex("idx_age", false, "age") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "age", Op = IntentOp.Between, IntegerValue = 20, HighValue = 30, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.Equal(IntentOp.Between, plan.SeekOp);
        Assert.Equal(20L, plan.SeekKey);
        Assert.Equal(30L, plan.UpperBound);
        Assert.True(plan.HasUpperBound);
    }

    [Fact]
    public void SelectBestIndex_GtCondition_ReturnsPlanNoUpperBound()
    {
        var indexes = new[] { MakeIndex("idx_age", false, "age") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "age", Op = IntentOp.Gt, IntegerValue = 25, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.Equal(IntentOp.Gt, plan.SeekOp);
        Assert.Equal(25L, plan.SeekKey);
        Assert.False(plan.HasUpperBound);
    }

    [Fact]
    public void SelectBestIndex_EmptyConditions_ReturnsEmptyPlan()
    {
        var indexes = new[] { MakeIndex("idx_id", false, "id") };
        var conditions = new List<SargableCondition>();

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.Null(plan.Index);
    }

    [Fact]
    public void SelectBestIndex_EmptyIndexes_ReturnsEmptyPlan()
    {
        var indexes = Array.Empty<IndexInfo>();
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "id", Op = IntentOp.Eq, IntegerValue = 1, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.Null(plan.Index);
    }

    [Fact]
    public void SelectBestIndex_TextEqCondition_ReturnsPlanWithTextKey()
    {
        var indexes = new[] { MakeIndex("idx_sha", false, "sha") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "sha", Op = IntentOp.Eq, IsTextKey = true, TextValue = "abc123" }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
        Assert.True(plan.IsTextKey);
        Assert.Equal("abc123", plan.TextValue);
        Assert.Equal("sha", plan.ConsumedColumn);
    }

    [Fact]
    public void SelectBestIndex_CaseInsensitiveColumnMatch()
    {
        var indexes = new[] { MakeIndex("idx_user_id", false, "User_Id") };
        var conditions = new List<SargableCondition>
        {
            new() { ColumnName = "user_id", Op = IntentOp.Eq, IntegerValue = 42, IsIntegerKey = true }
        };

        var plan = IndexSelector.SelectBestIndex(conditions, indexes);

        Assert.NotNull(plan.Index);
    }
}
