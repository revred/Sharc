// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Intent;
using Xunit;
using IntNode = Sharc.Query.Intent.PredicateNode;

namespace Sharc.Tests.Query;

public class IntentToFilterBridgeTests
{
    // ─── Simple comparisons ─────────────────────────────────────

    [Fact]
    public void Bridge_Eq_Long_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.Eq, ColumnName = "age", Value = IntentValue.FromInt64(18L) }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Eq_String_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.Eq, ColumnName = "status", Value = IntentValue.FromText("active") }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Gt_Double_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.Gt, ColumnName = "score", Value = IntentValue.FromFloat64(3.14) }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── All comparison ops ─────────────────────────────────────

    [Theory]
    [InlineData(IntentOp.Eq)]
    [InlineData(IntentOp.Neq)]
    [InlineData(IntentOp.Lt)]
    [InlineData(IntentOp.Lte)]
    [InlineData(IntentOp.Gt)]
    [InlineData(IntentOp.Gte)]
    public void Bridge_ComparisonOps_AllProduce(IntentOp op)
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = op, ColumnName = "col", Value = IntentValue.FromInt64(42L) }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── NULL tests ─────────────────────────────────────────────

    [Fact]
    public void Bridge_IsNull_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.IsNull, ColumnName = "email" }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_IsNotNull_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.IsNotNull, ColumnName = "email" }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── Between ────────────────────────────────────────────────

    [Fact]
    public void Bridge_Between_Long_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode
            {
                Op = IntentOp.Between, ColumnName = "age",
                Value = IntentValue.FromInt64(18L), HighValue = IntentValue.FromInt64(65L)
            }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── String operations ──────────────────────────────────────

    [Fact]
    public void Bridge_StartsWith_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.StartsWith, ColumnName = "name", Value = IntentValue.FromText("Al") }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_EndsWith_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.EndsWith, ColumnName = "name", Value = IntentValue.FromText("son") }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Contains_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode { Op = IntentOp.Contains, ColumnName = "name", Value = IntentValue.FromText("ob") }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── IN ─────────────────────────────────────────────────────

    [Fact]
    public void Bridge_In_LongSet_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode
            {
                Op = IntentOp.In, ColumnName = "id",
                Value = IntentValue.FromInt64Set([1L, 2L, 3L])
            }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_In_TextSet_ProducesFilter()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [new IntNode
            {
                Op = IntentOp.In, ColumnName = "status",
                Value = IntentValue.FromTextSet(["active", "pending"])
            }],
            rootIndex: 0));

        Assert.NotNull(filter);
    }

    // ─── Compound: AND, OR, NOT ─────────────────────────────────

    [Fact]
    public void Bridge_And_CombinesChildren()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [
                new IntNode { Op = IntentOp.Gt, ColumnName = "age", Value = IntentValue.FromInt64(18L) },
                new IntNode { Op = IntentOp.Eq, ColumnName = "status", Value = IntentValue.FromText("active") },
                new IntNode { Op = IntentOp.And, LeftIndex = 0, RightIndex = 1 },
            ],
            rootIndex: 2));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Or_CombinesChildren()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [
                new IntNode { Op = IntentOp.Eq, ColumnName = "a", Value = IntentValue.FromInt64(1L) },
                new IntNode { Op = IntentOp.Eq, ColumnName = "b", Value = IntentValue.FromInt64(2L) },
                new IntNode { Op = IntentOp.Or, LeftIndex = 0, RightIndex = 1 },
            ],
            rootIndex: 2));

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Not_WrapsChild()
    {
        var filter = IntentToFilterBridge.Build(new PredicateIntent(
            [
                new IntNode { Op = IntentOp.Gt, ColumnName = "age", Value = IntentValue.FromInt64(18L) },
                new IntNode { Op = IntentOp.Not, LeftIndex = 0 },
            ],
            rootIndex: 1));

        Assert.NotNull(filter);
    }

    // ─── Parameters ──────────────────────────────────────────────

    [Fact]
    public void Bridge_Parameter_ResolvesFromDictionary()
    {
        var filter = IntentToFilterBridge.Build(
            new PredicateIntent(
                [new IntNode { Op = IntentOp.Eq, ColumnName = "age", Value = IntentValue.FromParameter("minAge") }],
                rootIndex: 0),
            new Dictionary<string, object> { ["minAge"] = 18L });

        Assert.NotNull(filter);
    }

    [Fact]
    public void Bridge_Parameter_MissingKey_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            IntentToFilterBridge.Build(
                new PredicateIntent(
                    [new IntNode { Op = IntentOp.Eq, ColumnName = "age", Value = IntentValue.FromParameter("minAge") }],
                    rootIndex: 0),
                new Dictionary<string, object>()));
    }

    // ─── End-to-end from Sharq string ───────────────────────────

    [Fact]
    public void Bridge_EndToEnd_FromSharqString()
    {
        var intent = IntentCompiler.Compile(
            "SELECT * FROM users WHERE age > 18 AND status = 'active'");

        Assert.NotNull(intent.Filter);
        var filter = IntentToFilterBridge.Build(intent.Filter.Value);
        Assert.NotNull(filter);
    }
}
