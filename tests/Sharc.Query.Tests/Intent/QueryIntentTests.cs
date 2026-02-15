// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Query.Tests.Intent;

public class QueryIntentTests
{
    // ─── IntentOp coverage ──────────────────────────────────────────

    [Theory]
    [InlineData(IntentOp.Eq, 0)]
    [InlineData(IntentOp.Neq, 1)]
    [InlineData(IntentOp.Lt, 2)]
    [InlineData(IntentOp.Lte, 3)]
    [InlineData(IntentOp.Gt, 4)]
    [InlineData(IntentOp.Gte, 5)]
    [InlineData(IntentOp.Between, 6)]
    [InlineData(IntentOp.IsNull, 10)]
    [InlineData(IntentOp.IsNotNull, 11)]
    [InlineData(IntentOp.StartsWith, 20)]
    [InlineData(IntentOp.EndsWith, 21)]
    [InlineData(IntentOp.Contains, 22)]
    [InlineData(IntentOp.In, 30)]
    [InlineData(IntentOp.NotIn, 31)]
    [InlineData(IntentOp.Like, 40)]
    [InlineData(IntentOp.NotLike, 41)]
    [InlineData(IntentOp.And, 100)]
    [InlineData(IntentOp.Or, 101)]
    [InlineData(IntentOp.Not, 102)]
    public void IntentOp_HasExpectedValue(IntentOp op, int expected)
    {
        Assert.Equal((byte)expected, (byte)op);
    }

    // ─── PredicateNode ──────────────────────────────────────────────

    [Fact]
    public void PredicateNode_Leaf_HasNoChildren()
    {
        var node = new PredicateNode
        {
            Op = IntentOp.Eq,
            ColumnName = "age",
            Value = IntentValue.FromInt64(18L),
        };

        Assert.Equal(IntentOp.Eq, node.Op);
        Assert.Equal("age", node.ColumnName);
        Assert.Equal(18L, node.Value.AsInt64);
        Assert.Equal(-1, node.LeftIndex);
        Assert.Equal(-1, node.RightIndex);
    }

    [Fact]
    public void PredicateNode_And_ReferencesChildren()
    {
        var node = new PredicateNode
        {
            Op = IntentOp.And,
            LeftIndex = 0,
            RightIndex = 1,
        };

        Assert.Equal(IntentOp.And, node.Op);
        Assert.Equal(0, node.LeftIndex);
        Assert.Equal(1, node.RightIndex);
    }

    [Fact]
    public void PredicateNode_Between_HasHighValue()
    {
        var node = new PredicateNode
        {
            Op = IntentOp.Between,
            ColumnName = "age",
            Value = IntentValue.FromInt64(18L),
            HighValue = IntentValue.FromInt64(65L),
        };

        Assert.Equal(18L, node.Value.AsInt64);
        Assert.Equal(65L, node.HighValue.AsInt64);
    }

    // ─── PredicateIntent ────────────────────────────────────────────

    [Fact]
    public void PredicateIntent_SingleNode_RootIsZero()
    {
        var nodes = new[]
        {
            new PredicateNode
            {
                Op = IntentOp.Eq,
                ColumnName = "id",
                Value = IntentValue.FromInt64(1L),
            }
        };

        var intent = new PredicateIntent(nodes, rootIndex: 0);
        Assert.Equal(0, intent.RootIndex);
        Assert.Single(intent.Nodes);
    }

    [Fact]
    public void PredicateIntent_CompoundAnd_PostOrder()
    {
        // age > 18 AND status = 'active'
        // Post-order: [leaf0, leaf1, and-root]
        var nodes = new[]
        {
            new PredicateNode { Op = IntentOp.Gt, ColumnName = "age", Value = IntentValue.FromInt64(18L) },
            new PredicateNode { Op = IntentOp.Eq, ColumnName = "status", Value = IntentValue.FromText("active") },
            new PredicateNode { Op = IntentOp.And, LeftIndex = 0, RightIndex = 1 },
        };

        var intent = new PredicateIntent(nodes, rootIndex: 2);
        Assert.Equal(3, intent.Nodes.Length);
        Assert.Equal(2, intent.RootIndex);
        Assert.Equal(IntentOp.And, intent.Nodes[intent.RootIndex].Op);
    }

    // ─── OrderIntent ────────────────────────────────────────────────

    [Fact]
    public void OrderIntent_Default_Ascending()
    {
        var order = new OrderIntent { ColumnName = "name" };
        Assert.Equal("name", order.ColumnName);
        Assert.False(order.Descending);
        Assert.False(order.NullsFirst);
    }

    [Fact]
    public void OrderIntent_Descending_NullsFirst()
    {
        var order = new OrderIntent { ColumnName = "age", Descending = true, NullsFirst = true };
        Assert.True(order.Descending);
        Assert.True(order.NullsFirst);
    }

    // ─── QueryIntent ────────────────────────────────────────────────

    [Fact]
    public void QueryIntent_MinimalSelect_AllColumns()
    {
        var intent = new QueryIntent { TableName = "users" };
        Assert.Equal("users", intent.TableName);
        Assert.Null(intent.Columns);
        Assert.Null(intent.Filter);
        Assert.Null(intent.OrderBy);
        Assert.Null(intent.Limit);
        Assert.Null(intent.Offset);
        Assert.False(intent.IsDistinct);
    }

    [Fact]
    public void QueryIntent_FullQuery_AllFieldsSet()
    {
        var filter = new PredicateIntent(
            [new PredicateNode { Op = IntentOp.Gt, ColumnName = "age", Value = IntentValue.FromInt64(18L) }],
            rootIndex: 0);

        var intent = new QueryIntent
        {
            TableName = "users",
            Columns = ["name", "age"],
            Filter = filter,
            OrderBy = [new OrderIntent { ColumnName = "name" }],
            Limit = 10,
            Offset = 20,
            IsDistinct = true,
        };

        Assert.Equal("users", intent.TableName);
        Assert.Equal(2, intent.Columns!.Count);
        Assert.NotNull(intent.Filter);
        Assert.Single(intent.OrderBy!);
        Assert.Equal(10L, intent.Limit);
        Assert.Equal(20L, intent.Offset);
        Assert.True(intent.IsDistinct);
    }

    [Fact]
    public void QueryIntent_WithRecordId_StoresId()
    {
        var intent = new QueryIntent { TableName = "person", TableRecordId = "alice" };
        Assert.Equal("person", intent.TableName);
        Assert.Equal("alice", intent.TableRecordId);
    }
}
