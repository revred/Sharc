// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Query.Tests.Intent;

public class IntentCompilerTests
{
    private static QueryIntent Compile(string sharq) =>
        IntentCompiler.Compile(sharq);

    // ─── Table + columns ────────────────────────────────────────

    [Fact]
    public void Compile_SelectStar_AllColumns()
    {
        var intent = Compile("SELECT * FROM users");
        Assert.Equal("users", intent.TableName);
        Assert.Null(intent.Columns);
    }

    [Fact]
    public void Compile_SpecificColumns_Projected()
    {
        var intent = Compile("SELECT name, age FROM users");
        Assert.Equal("users", intent.TableName);
        Assert.NotNull(intent.Columns);
        Assert.Equal(2, intent.Columns!.Count);
        Assert.Equal("name", intent.Columns[0]);
        Assert.Equal("age", intent.Columns[1]);
    }

    [Fact]
    public void Compile_Distinct_FlagSet()
    {
        var intent = Compile("SELECT DISTINCT name FROM users");
        Assert.True(intent.IsDistinct);
    }

    [Fact]
    public void Compile_RecordIdTable_ParsedCorrectly()
    {
        var intent = Compile("SELECT * FROM person:alice");
        Assert.Equal("person", intent.TableName);
        Assert.Equal("alice", intent.TableRecordId);
    }

    // ─── Simple WHERE predicates ────────────────────────────────

    [Fact]
    public void Compile_WhereEqInteger_SingleNode()
    {
        var intent = Compile("SELECT * FROM users WHERE age = 18");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Eq, root.Op);
        Assert.Equal("age", root.ColumnName);
        Assert.Equal(IntentValueKind.Signed64, root.Value.Kind);
        Assert.Equal(18L, root.Value.AsInt64);
    }

    [Fact]
    public void Compile_WhereGtFloat_SingleNode()
    {
        var intent = Compile("SELECT * FROM t WHERE score > 3.14");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Gt, root.Op);
        Assert.Equal("score", root.ColumnName);
        Assert.Equal(IntentValueKind.Real, root.Value.Kind);
        Assert.Equal(3.14, root.Value.AsFloat64);
    }

    [Fact]
    public void Compile_WhereEqString_SingleNode()
    {
        var intent = Compile("SELECT * FROM users WHERE status = 'active'");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Eq, root.Op);
        Assert.Equal("status", root.ColumnName);
        Assert.Equal("active", root.Value.AsText);
    }

    [Fact]
    public void Compile_WhereEqParameter_PreservesParam()
    {
        var intent = Compile("SELECT * FROM users WHERE id = $userId");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Eq, root.Op);
        Assert.Equal(IntentValueKind.Parameter, root.Value.Kind);
        Assert.Equal("userId", root.Value.AsText);
    }

    [Fact]
    public void Compile_WhereIsNull_SingleNode()
    {
        var intent = Compile("SELECT * FROM users WHERE email IS NULL");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.IsNull, root.Op);
        Assert.Equal("email", root.ColumnName);
    }

    [Fact]
    public void Compile_WhereIsNotNull_SingleNode()
    {
        var intent = Compile("SELECT * FROM users WHERE email IS NOT NULL");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.IsNotNull, root.Op);
        Assert.Equal("email", root.ColumnName);
    }

    // ─── Comparison operators ───────────────────────────────────

    [Theory]
    [InlineData("age < 18", IntentOp.Lt)]
    [InlineData("age <= 18", IntentOp.Lte)]
    [InlineData("age > 18", IntentOp.Gt)]
    [InlineData("age >= 18", IntentOp.Gte)]
    [InlineData("age != 18", IntentOp.Neq)]
    public void Compile_ComparisonOps_CorrectIntentOp(string where, IntentOp expected)
    {
        var intent = Compile($"SELECT * FROM t WHERE {where}");
        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(expected, root.Op);
    }

    // ─── BETWEEN ────────────────────────────────────────────────

    [Fact]
    public void Compile_Between_HasHighValue()
    {
        var intent = Compile("SELECT * FROM t WHERE age BETWEEN 18 AND 65");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Between, root.Op);
        Assert.Equal("age", root.ColumnName);
        Assert.Equal(18L, root.Value.AsInt64);
        Assert.Equal(65L, root.HighValue.AsInt64);
    }

    // ─── IN ─────────────────────────────────────────────────────

    [Fact]
    public void Compile_InIntegers_IntegerSet()
    {
        var intent = Compile("SELECT * FROM t WHERE id IN (1, 2, 3)");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.In, root.Op);
        Assert.Equal("id", root.ColumnName);
        Assert.Equal(IntentValueKind.Signed64Set, root.Value.Kind);
#pragma warning disable CA1861
        Assert.Equal(new long[] { 1L, 2L, 3L }, root.Value.AsInt64Set);
#pragma warning restore CA1861
    }

    [Fact]
    public void Compile_InStrings_TextSet()
    {
        var intent = Compile("SELECT * FROM t WHERE status IN ('a', 'b', 'c')");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.In, root.Op);
        Assert.Equal(IntentValueKind.TextSet, root.Value.Kind);
#pragma warning disable CA1861
        Assert.Equal(new string[] { "a", "b", "c" }, root.Value.AsTextSet);
#pragma warning restore CA1861
    }

    [Fact]
    public void Compile_NotIn_NegatedOp()
    {
        var intent = Compile("SELECT * FROM t WHERE id NOT IN (1, 2)");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.NotIn, root.Op);
    }

    // ─── LIKE → StartsWith / EndsWith / Contains ────────────────

    [Fact]
    public void Compile_LikePrefix_StartsWith()
    {
        var intent = Compile("SELECT * FROM t WHERE name LIKE 'Al%'");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.StartsWith, root.Op);
        Assert.Equal("name", root.ColumnName);
        Assert.Equal("Al", root.Value.AsText);
    }

    [Fact]
    public void Compile_LikeSuffix_EndsWith()
    {
        var intent = Compile("SELECT * FROM t WHERE name LIKE '%son'");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.EndsWith, root.Op);
        Assert.Equal("son", root.Value.AsText);
    }

    [Fact]
    public void Compile_LikeContains_Contains()
    {
        var intent = Compile("SELECT * FROM t WHERE name LIKE '%ob%'");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Contains, root.Op);
        Assert.Equal("ob", root.Value.AsText);
    }

    [Fact]
    public void Compile_LikeComplex_FallsBackToLike()
    {
        var intent = Compile("SELECT * FROM t WHERE name LIKE 'A%b%'");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Like, root.Op);
        Assert.Equal("A%b%", root.Value.AsText);
    }

    // ─── Compound: AND / OR ─────────────────────────────────────

    [Fact]
    public void Compile_And_PostOrderLayout()
    {
        var intent = Compile("SELECT * FROM t WHERE age > 18 AND status = 'active'");
        Assert.NotNull(intent.Filter);

        var nodes = intent.Filter!.Value.Nodes;
        var root = nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.And, root.Op);

        // Children emitted before root
        Assert.True(root.LeftIndex < intent.Filter.Value.RootIndex);
        Assert.True(root.RightIndex < intent.Filter.Value.RootIndex);

        Assert.Equal(IntentOp.Gt, nodes[root.LeftIndex].Op);
        Assert.Equal(IntentOp.Eq, nodes[root.RightIndex].Op);
    }

    [Fact]
    public void Compile_Or_PostOrderLayout()
    {
        var intent = Compile("SELECT * FROM t WHERE a = 1 OR b = 2");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Or, root.Op);
    }

    [Fact]
    public void Compile_Not_UnaryNode()
    {
        var intent = Compile("SELECT * FROM t WHERE NOT age > 18");
        Assert.NotNull(intent.Filter);

        var root = intent.Filter!.Value.Nodes[intent.Filter.Value.RootIndex];
        Assert.Equal(IntentOp.Not, root.Op);
        Assert.True(root.LeftIndex >= 0);
        Assert.Equal(-1, root.RightIndex);
    }

    // ─── ORDER BY ───────────────────────────────────────────────

    [Fact]
    public void Compile_OrderByAsc_Default()
    {
        var intent = Compile("SELECT * FROM users ORDER BY name");
        Assert.NotNull(intent.OrderBy);
        Assert.Single(intent.OrderBy!);
        Assert.Equal("name", intent.OrderBy[0].ColumnName);
        Assert.False(intent.OrderBy[0].Descending);
    }

    [Fact]
    public void Compile_OrderByDesc_FlagSet()
    {
        var intent = Compile("SELECT * FROM users ORDER BY age DESC");
        Assert.NotNull(intent.OrderBy);
        Assert.True(intent.OrderBy![0].Descending);
    }

    [Fact]
    public void Compile_OrderByMultiple_All()
    {
        var intent = Compile("SELECT * FROM users ORDER BY name ASC, age DESC");
        Assert.NotNull(intent.OrderBy);
        Assert.Equal(2, intent.OrderBy!.Count);
        Assert.False(intent.OrderBy[0].Descending);
        Assert.True(intent.OrderBy[1].Descending);
    }

    // ─── LIMIT / OFFSET ────────────────────────────────────────

    [Fact]
    public void Compile_Limit_StoresValue()
    {
        var intent = Compile("SELECT * FROM users LIMIT 10");
        Assert.Equal(10L, intent.Limit);
        Assert.Null(intent.Offset);
    }

    [Fact]
    public void Compile_LimitOffset_BothStored()
    {
        var intent = Compile("SELECT * FROM users LIMIT 10 OFFSET 20");
        Assert.Equal(10L, intent.Limit);
        Assert.Equal(20L, intent.Offset);
    }

    // ─── No WHERE ───────────────────────────────────────────────

    [Fact]
    public void Compile_NoWhere_NullFilter()
    {
        var intent = Compile("SELECT * FROM users");
        Assert.Null(intent.Filter);
    }

    // ─── Aggregation ──────────────────────────────────────────────

    [Fact]
    public void Compile_CountStar_ProducesAggregate()
    {
        var intent = Compile("SELECT COUNT(*) FROM users");
        Assert.NotNull(intent.Aggregates);
        Assert.Single(intent.Aggregates!);
        Assert.Equal(AggregateFunction.CountStar, intent.Aggregates[0].Function);
        Assert.Null(intent.Aggregates[0].ColumnName);
        Assert.Equal("COUNT(*)", intent.Aggregates[0].Alias);
    }

    [Fact]
    public void Compile_SumColumn_ProducesAggregate()
    {
        var intent = Compile("SELECT SUM(age) FROM users");
        Assert.NotNull(intent.Aggregates);
        Assert.Single(intent.Aggregates!);
        Assert.Equal(AggregateFunction.Sum, intent.Aggregates[0].Function);
        Assert.Equal("age", intent.Aggregates[0].ColumnName);
    }

    [Fact]
    public void Compile_AvgColumn_ProducesAggregate()
    {
        var intent = Compile("SELECT AVG(salary) FROM employees");
        Assert.NotNull(intent.Aggregates);
        Assert.Equal(AggregateFunction.Avg, intent.Aggregates![0].Function);
    }

    [Fact]
    public void Compile_MinMax_ProducesAggregates()
    {
        var intent = Compile("SELECT MIN(age), MAX(age) FROM users");
        Assert.NotNull(intent.Aggregates);
        Assert.Equal(2, intent.Aggregates!.Count);
        Assert.Equal(AggregateFunction.Min, intent.Aggregates[0].Function);
        Assert.Equal(AggregateFunction.Max, intent.Aggregates[1].Function);
    }

    [Fact]
    public void Compile_GroupBy_PopulatesGroupByList()
    {
        var intent = Compile("SELECT dept, COUNT(*) FROM employees GROUP BY dept");
        Assert.NotNull(intent.GroupBy);
        Assert.Single(intent.GroupBy!);
        Assert.Equal("dept", intent.GroupBy[0]);
    }

    [Fact]
    public void Compile_GroupByWithHaving_PopulatesHaving()
    {
        var intent = Compile("SELECT dept, COUNT(*) FROM employees GROUP BY dept HAVING COUNT(*) > 5");
        Assert.NotNull(intent.GroupBy);
        Assert.NotNull(intent.HavingFilter);
    }

    // ─── Unsupported query guards ─────────────────────────────────

    [Fact]
    public void Compile_UnionQuery_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Compile("SELECT * FROM users UNION SELECT * FROM orders"));
    }

    [Fact]
    public void Compile_IntersectQuery_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Compile("SELECT * FROM users INTERSECT SELECT * FROM orders"));
    }

    [Fact]
    public void Compile_CteQuery_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Compile("WITH cte AS (SELECT * FROM users) SELECT * FROM cte"));
    }

    // ─── CompilePlan: compound queries ─────────────────────────────

    private static QueryPlan CompilePlan(string sharq) =>
        IntentCompiler.CompilePlan(sharq);

    [Fact]
    public void CompilePlan_SimpleSelect_ReturnsSimplePlan()
    {
        var plan = CompilePlan("SELECT * FROM users");
        Assert.False(plan.IsCompound);
        Assert.NotNull(plan.Simple);
        Assert.Null(plan.Compound);
        Assert.Equal("users", plan.Simple!.TableName);
    }

    [Fact]
    public void CompilePlan_Union_ReturnsCompoundPlan()
    {
        var plan = CompilePlan("SELECT name FROM users UNION SELECT name FROM orders");
        Assert.True(plan.IsCompound);
        Assert.NotNull(plan.Compound);
        Assert.Null(plan.Simple);
        Assert.Equal(CompoundOperator.Union, plan.Compound!.Operator);
        Assert.Equal("users", plan.Compound.Left.TableName);
        Assert.NotNull(plan.Compound.RightSimple);
        Assert.Equal("orders", plan.Compound.RightSimple!.TableName);
    }

    [Fact]
    public void CompilePlan_UnionAll_SetsOperator()
    {
        var plan = CompilePlan("SELECT * FROM t1 UNION ALL SELECT * FROM t2");
        Assert.True(plan.IsCompound);
        Assert.Equal(CompoundOperator.UnionAll, plan.Compound!.Operator);
    }

    [Fact]
    public void CompilePlan_Intersect_SetsOperator()
    {
        var plan = CompilePlan("SELECT * FROM t1 INTERSECT SELECT * FROM t2");
        Assert.True(plan.IsCompound);
        Assert.Equal(CompoundOperator.Intersect, plan.Compound!.Operator);
    }

    [Fact]
    public void CompilePlan_Except_SetsOperator()
    {
        var plan = CompilePlan("SELECT * FROM t1 EXCEPT SELECT * FROM t2");
        Assert.True(plan.IsCompound);
        Assert.Equal(CompoundOperator.Except, plan.Compound!.Operator);
    }

    [Fact]
    public void CompilePlan_ThreeWayUnion_ChainsRightRecursively()
    {
        var plan = CompilePlan("SELECT * FROM t1 UNION SELECT * FROM t2 UNION SELECT * FROM t3");
        Assert.True(plan.IsCompound);
        Assert.Equal("t1", plan.Compound!.Left.TableName);
        Assert.Null(plan.Compound.RightSimple);
        Assert.NotNull(plan.Compound.RightCompound);

        var inner = plan.Compound.RightCompound!;
        Assert.Equal("t2", inner.Left.TableName);
        Assert.NotNull(inner.RightSimple);
        Assert.Equal("t3", inner.RightSimple!.TableName);
    }

    [Fact]
    public void CompilePlan_UnionWithOrderBy_HoistsToFinalOrderBy()
    {
        var plan = CompilePlan("SELECT name FROM t1 UNION SELECT name FROM t2 ORDER BY name");
        Assert.True(plan.IsCompound);
        Assert.NotNull(plan.Compound!.FinalOrderBy);
        Assert.Single(plan.Compound.FinalOrderBy!);
        Assert.Equal("name", plan.Compound.FinalOrderBy[0].ColumnName);

        // The rightmost leaf should NOT have OrderBy (it was hoisted)
        Assert.Null(plan.Compound.RightSimple!.OrderBy);
    }

    [Fact]
    public void CompilePlan_UnionWithLimitOffset_HoistsToFinal()
    {
        var plan = CompilePlan("SELECT * FROM t1 UNION SELECT * FROM t2 LIMIT 10 OFFSET 5");
        Assert.True(plan.IsCompound);
        Assert.Equal(10L, plan.Compound!.FinalLimit);
        Assert.Equal(5L, plan.Compound.FinalOffset);

        // The rightmost leaf should NOT have Limit/Offset (they were hoisted)
        Assert.Null(plan.Compound.RightSimple!.Limit);
        Assert.Null(plan.Compound.RightSimple.Offset);
    }

    [Fact]
    public void CompilePlan_SingleCote_ProducesCoteIntent()
    {
        var plan = CompilePlan("WITH cte AS (SELECT * FROM users) SELECT * FROM cte");
        Assert.True(plan.HasCotes);
        Assert.Single(plan.Cotes!);
        Assert.Equal("cte", plan.Cotes![0].Name);
        Assert.Equal("users", plan.Cotes![0].Query.Simple!.TableName);

        Assert.False(plan.IsCompound);
        Assert.NotNull(plan.Simple);
        Assert.Equal("cte", plan.Simple!.TableName);
    }

    [Fact]
    public void CompilePlan_MultipleCotes_AllCompiled()
    {
        var plan = CompilePlan(
            "WITH a AS (SELECT * FROM t1), b AS (SELECT * FROM t2) SELECT * FROM a");
        Assert.True(plan.HasCotes);
        Assert.Equal(2, plan.Cotes!.Count);
        Assert.Equal("a", plan.Cotes[0].Name);
        Assert.Equal("t1", plan.Cotes[0].Query.Simple!.TableName);
        Assert.Equal("b", plan.Cotes[1].Name);
        Assert.Equal("t2", plan.Cotes[1].Query.Simple!.TableName);
    }

    // ─── Negative path tests (P0 Item 20) ───────────────────────

    [Fact]
    public void Compile_UnsupportedAggregateFunction_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Compile("SELECT MEDIAN(age) FROM users"));
    }

    [Fact]
    public void Compile_LikeWithNonStringPattern_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Compile("SELECT * FROM users WHERE name LIKE 42"));
    }

    [Fact]
    public void Compile_InWithNonLiteralValues_ThrowsNotSupported()
    {
        // IN list with only a parameter — not supported
        Assert.Throws<NotSupportedException>(() =>
            Compile("SELECT * FROM users WHERE age IN ($param)"));
    }

    [Fact]
    public void Compile_OrderByColumnName_PopulatesOrderBy()
    {
        var intent = Compile("SELECT * FROM users ORDER BY name ASC");
        Assert.NotNull(intent.OrderBy);
        Assert.Single(intent.OrderBy!);
        Assert.Equal("name", intent.OrderBy![0].ColumnName);
        Assert.False(intent.OrderBy[0].Descending);
    }

    [Fact]
    public void Compile_OrderByDescending_SetsFlag()
    {
        var intent = Compile("SELECT * FROM users ORDER BY age DESC");
        Assert.NotNull(intent.OrderBy);
        Assert.True(intent.OrderBy![0].Descending);
    }

    [Fact]
    public void Compile_LimitAndOffset_BothPopulated()
    {
        var intent = Compile("SELECT * FROM users LIMIT 10 OFFSET 20");
        Assert.Equal(10L, intent.Limit);
        Assert.Equal(20L, intent.Offset);
    }

    [Fact]
    public void Compile_GroupByWithoutAggregate_PopulatesGroupBy()
    {
        var intent = Compile("SELECT dept FROM employees GROUP BY dept");
        Assert.NotNull(intent.GroupBy);
        Assert.Single(intent.GroupBy!);
        Assert.Equal("dept", intent.GroupBy![0]);
    }

    [Fact]
    public void CompilePlan_CompoundWithFinalOrderByAndLimit_Hoisted()
    {
        var plan = CompilePlan(
            "SELECT * FROM t1 UNION ALL SELECT * FROM t2 ORDER BY id LIMIT 5");
        Assert.True(plan.IsCompound);
        Assert.NotNull(plan.Compound!.FinalOrderBy);
        Assert.Equal(5L, plan.Compound.FinalLimit);
    }

    // ─── Execution Hint Propagation ──────────────────────────────────

    [Fact]
    public void Compile_NoHint_DefaultsDirect()
    {
        var intent = Compile("SELECT * FROM users");
        Assert.Equal(ExecutionHint.Direct, intent.Hint);
    }

    [Fact]
    public void Compile_CachedHint_PropagatedToIntent()
    {
        var intent = Compile("CACHED SELECT * FROM users WHERE age > 25");
        Assert.Equal(ExecutionHint.Cached, intent.Hint);
    }

    [Fact]
    public void Compile_JitHint_PropagatedToIntent()
    {
        var intent = Compile("JIT SELECT name FROM users");
        Assert.Equal(ExecutionHint.Jit, intent.Hint);
    }

    [Fact]
    public void CompilePlan_CachedHint_PropagatedToPlan()
    {
        var plan = CompilePlan("CACHED SELECT * FROM users");
        Assert.Equal(ExecutionHint.Cached, plan.Hint);
        Assert.Equal(ExecutionHint.Cached, plan.Simple!.Hint);
    }

    [Fact]
    public void CompilePlan_JitHint_PropagatedToPlan()
    {
        var plan = CompilePlan("JIT SELECT * FROM users");
        Assert.Equal(ExecutionHint.Jit, plan.Hint);
    }
}
