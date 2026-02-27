// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using PN = Sharc.Query.Intent.PredicateNode;
using Sharc.Core.Schema;
using Sharc.Query.Intent;
using Sharc.Query.Optimization;
using Xunit;

namespace Sharc.Tests.Query;

public class PredicateAnalyzerTests
{
    [Fact]
    public void ExtractSargable_EqOnInteger_ExtractsCondition()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "user_id", Value = IntentValue.FromInt64(42) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.Equal("user_id", conditions[0].ColumnName);
        Assert.Equal(IntentOp.Eq, conditions[0].Op);
        Assert.Equal(42L, conditions[0].IntegerValue);
        Assert.True(conditions[0].IsIntegerKey);
    }

    [Fact]
    public void ExtractSargable_GtOnInteger_ExtractsCondition()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Gt, ColumnName = "age", Value = IntentValue.FromInt64(25) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.Equal("age", conditions[0].ColumnName);
        Assert.Equal(IntentOp.Gt, conditions[0].Op);
        Assert.Equal(25L, conditions[0].IntegerValue);
    }

    [Fact]
    public void ExtractSargable_GteOnInteger_ExtractsCondition()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Gte, ColumnName = "id", Value = IntentValue.FromInt64(10) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.Equal(IntentOp.Gte, conditions[0].Op);
        Assert.Equal(10L, conditions[0].IntegerValue);
    }

    [Fact]
    public void ExtractSargable_LtOnInteger_ExtractsCondition()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Lt, ColumnName = "id", Value = IntentValue.FromInt64(100) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.Equal(IntentOp.Lt, conditions[0].Op);
    }

    [Fact]
    public void ExtractSargable_BetweenOnInteger_ExtractsWithBothBounds()
    {
        var nodes = new PN[]
        {
            new()
            {
                Op = IntentOp.Between, ColumnName = "age",
                Value = IntentValue.FromInt64(20), HighValue = IntentValue.FromInt64(30)
            }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.Equal(IntentOp.Between, conditions[0].Op);
        Assert.Equal(20L, conditions[0].IntegerValue);
        Assert.Equal(30L, conditions[0].HighValue);
    }

    [Fact]
    public void ExtractSargable_TextEquality_NotExtractedForInteger()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "name", Value = IntentValue.FromText("Alice") }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        // Text conditions not extracted as integer sargable
        var intConds = conditions.Where(c => c.IsIntegerKey).ToList();
        Assert.Empty(intConds);
    }

    [Fact]
    public void ExtractSargable_AndOfMixed_ExtractsOnlyIntegerConditions()
    {
        // WHERE user_id = 42 AND name = 'Alice'
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "user_id", Value = IntentValue.FromInt64(42) },  // 0
            new() { Op = IntentOp.Eq, ColumnName = "name", Value = IntentValue.FromText("Alice") }, // 1
            new() { Op = IntentOp.And, LeftIndex = 0, RightIndex = 1 }                              // 2
        };
        var filter = new PredicateIntent(nodes, 2);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        var intConds = conditions.Where(c => c.IsIntegerKey).ToList();
        Assert.Single(intConds);
        Assert.Equal("user_id", intConds[0].ColumnName);
        Assert.Equal(42L, intConds[0].IntegerValue);
    }

    [Fact]
    public void ExtractSargable_OrBranch_NoExtraction()
    {
        // WHERE user_id = 42 OR user_id = 43
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "user_id", Value = IntentValue.FromInt64(42) },
            new() { Op = IntentOp.Eq, ColumnName = "user_id", Value = IntentValue.FromInt64(43) },
            new() { Op = IntentOp.Or, LeftIndex = 0, RightIndex = 1 }
        };
        var filter = new PredicateIntent(nodes, 2);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        // OR cannot be used for a single index seek
        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_StripsAliasPrefix()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "u.user_id", Value = IntentValue.FromInt64(42) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, "u");

        Assert.Single(conditions);
        Assert.Equal("user_id", conditions[0].ColumnName);
    }

    [Fact]
    public void ExtractSargable_WrongAlias_NotStripped()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "o.user_id", Value = IntentValue.FromInt64(42) }
        };
        var filter = new PredicateIntent(nodes, 0);

        // Alias "u" doesn't match prefix "o."
        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, "u");

        Assert.Single(conditions);
        Assert.Equal("o.user_id", conditions[0].ColumnName);
    }

    [Fact]
    public void ExtractSargable_NestedAndChain_ExtractsAllLeaves()
    {
        // WHERE a = 1 AND b = 2 AND c = 3
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "a", Value = IntentValue.FromInt64(1) }, // 0
            new() { Op = IntentOp.Eq, ColumnName = "b", Value = IntentValue.FromInt64(2) }, // 1
            new() { Op = IntentOp.And, LeftIndex = 0, RightIndex = 1 },                     // 2
            new() { Op = IntentOp.Eq, ColumnName = "c", Value = IntentValue.FromInt64(3) }, // 3
            new() { Op = IntentOp.And, LeftIndex = 2, RightIndex = 3 }                      // 4
        };
        var filter = new PredicateIntent(nodes, 4);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Equal(3, conditions.Count);
        Assert.Contains(conditions, c => c.ColumnName == "a" && c.IntegerValue == 1);
        Assert.Contains(conditions, c => c.ColumnName == "b" && c.IntegerValue == 2);
        Assert.Contains(conditions, c => c.ColumnName == "c" && c.IntegerValue == 3);
    }

    [Fact]
    public void ExtractSargable_NonSargableOps_Ignored()
    {
        // WHERE name LIKE '%foo%'
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Like, ColumnName = "name", Value = IntentValue.FromText("%foo%") }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_IsNull_Ignored()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.IsNull, ColumnName = "name" }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_NotWrappedCondition_Ignored()
    {
        // WHERE NOT (user_id = 42)
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "user_id", Value = IntentValue.FromInt64(42) },
            new() { Op = IntentOp.Not, LeftIndex = 0 }
        };
        var filter = new PredicateIntent(nodes, 1);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        // NOT negates the condition — can't use for seek
        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_EmptyNodes_ReturnsEmpty()
    {
        var filter = new PredicateIntent(Array.Empty<PN>(), -1);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_TextEquality_ExtractedAsTextKey()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "sha", Value = IntentValue.FromText("abc123") }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.True(conditions[0].IsTextKey);
        Assert.Equal("abc123", conditions[0].TextValue);
        Assert.False(conditions[0].IsIntegerKey);
    }

    [Fact]
    public void ExtractSargable_RealEq_ExtractsCondition()
    {
        var nodes = new PN[]
        {
            new() { Op = IntentOp.Eq, ColumnName = "score", Value = IntentValue.FromFloat64(42.5) }
        };
        var filter = new PredicateIntent(nodes, 0);

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, null);

        Assert.Single(conditions);
        Assert.True(conditions[0].IsRealKey);
        Assert.Equal(IntentOp.Eq, conditions[0].Op);
        Assert.Equal(42.5, conditions[0].RealValue);
    }

    [Fact]
    public void ExtractSargable_FilterStar_Int64Eq_ExtractsCondition()
    {
        IFilterStar filter = FilterStar.Column("user_id").Eq(42L);
        var columns = MakeColumns("id", "user_id", "name");

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, columns);

        Assert.Single(conditions);
        Assert.Equal("user_id", conditions[0].ColumnName);
        Assert.Equal(IntentOp.Eq, conditions[0].Op);
        Assert.True(conditions[0].IsIntegerKey);
        Assert.Equal(42L, conditions[0].IntegerValue);
    }

    [Fact]
    public void ExtractSargable_FilterStar_TextEq_ExtractsTextKey()
    {
        IFilterStar filter = FilterStar.Column("name").Eq("Alice");
        var columns = MakeColumns("id", "name");

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, columns);

        Assert.Single(conditions);
        Assert.True(conditions[0].IsTextKey);
        Assert.Equal("name", conditions[0].ColumnName);
        Assert.Equal("Alice", conditions[0].TextValue);
        Assert.Equal(IntentOp.Eq, conditions[0].Op);
    }

    [Fact]
    public void ExtractSargable_FilterStar_OrdinalBetween_ExtractsCondition()
    {
        IFilterStar filter = FilterStar.Column(1).Between(10L, 20L);
        var columns = MakeColumns("id", "age");

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, columns);

        Assert.Single(conditions);
        Assert.Equal("age", conditions[0].ColumnName);
        Assert.Equal(IntentOp.Between, conditions[0].Op);
        Assert.Equal(10L, conditions[0].IntegerValue);
        Assert.Equal(20L, conditions[0].HighValue);
    }

    [Fact]
    public void ExtractSargable_FilterStar_OrBranch_ReturnsEmpty()
    {
        IFilterStar filter = FilterStar.Or(
            FilterStar.Column("user_id").Eq(1L),
            FilterStar.Column("user_id").Eq(2L));
        var columns = MakeColumns("user_id");

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, columns);

        Assert.Empty(conditions);
    }

    [Fact]
    public void ExtractSargable_FilterStar_RealBetween_ExtractsCondition()
    {
        IFilterStar filter = FilterStar.Column("distance").Between(0.5, 1.5);
        var columns = MakeColumns("id", "distance");

        var conditions = PredicateAnalyzer.ExtractSargableConditions(filter, columns);

        Assert.Single(conditions);
        Assert.True(conditions[0].IsRealKey);
        Assert.Equal(IntentOp.Between, conditions[0].Op);
        Assert.Equal(0.5, conditions[0].RealValue);
        Assert.Equal(1.5, conditions[0].RealHighValue);
    }

    private static ColumnInfo[] MakeColumns(params string[] names)
    {
        var columns = new ColumnInfo[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            columns[i] = new ColumnInfo
            {
                Name = names[i],
                DeclaredType = "INTEGER",
                Ordinal = i,
                IsPrimaryKey = false,
                IsNotNull = false
            };
        }
        return columns;
    }
}
