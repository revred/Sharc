// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Tests.Filter;

public sealed class FilterTreeCompilerTests
{
    private static List<ColumnInfo> MakeColumns(params string[] names)
    {
        var list = new List<ColumnInfo>();
        for (int i = 0; i < names.Length; i++)
            list.Add(new ColumnInfo
            {
                Name = names[i],
                Ordinal = i,
                DeclaredType = "TEXT",
                IsPrimaryKey = false,
                IsNotNull = false
            });
        return list;
    }

    [Fact]
    public void Compile_SimplePredicate_ReturnsPredicateNode()
    {
        var columns = MakeColumns("id", "name", "age");
        var expr = FilterStar.Column("age").Gt(30);

        var node = FilterTreeCompiler.Compile(expr, columns);

        Assert.IsType<PredicateNode>(node);
    }

    [Fact]
    public void Compile_AndExpression_ReturnsAndNode()
    {
        var columns = MakeColumns("age", "score");
        var expr = FilterStar.And(
            FilterStar.Column("age").Gt(30),
            FilterStar.Column("score").Lt(50)
        );

        var node = FilterTreeCompiler.Compile(expr, columns);

        Assert.IsType<AndNode>(node);
    }

    [Fact]
    public void Compile_OrExpression_ReturnsOrNode()
    {
        var columns = MakeColumns("status");
        var expr = FilterStar.Or(
            FilterStar.Column("status").Eq("active"),
            FilterStar.Column("status").Eq("pending")
        );

        var node = FilterTreeCompiler.Compile(expr, columns);

        Assert.IsType<OrNode>(node);
    }

    [Fact]
    public void Compile_NotExpression_ReturnsNotNode()
    {
        var columns = MakeColumns("deleted");
        var expr = FilterStar.Not(FilterStar.Column("deleted").IsNotNull());

        var node = FilterTreeCompiler.Compile(expr, columns);

        Assert.IsType<NotNode>(node);
    }

    [Fact]
    public void Compile_ColumnNameResolution_CaseInsensitive()
    {
        var columns = MakeColumns("AGE");
        var expr = FilterStar.Column("age").Gt(30);

        // Should not throw — case-insensitive match
        var node = FilterTreeCompiler.Compile(expr, columns);
        Assert.NotNull(node);
    }

    [Fact]
    public void Compile_InvalidColumnName_ThrowsArgumentException()
    {
        var columns = MakeColumns("id", "name");
        var expr = FilterStar.Column("nonexistent").Gt(30);

        Assert.Throws<ArgumentException>(() => FilterTreeCompiler.Compile(expr, columns));
    }

    [Fact]
    public void Compile_ColumnByOrdinal_Works()
    {
        var columns = MakeColumns("id", "name", "age");
        var expr = FilterStar.Column(2).Gt(30);

        var node = FilterTreeCompiler.Compile(expr, columns);
        Assert.IsType<PredicateNode>(node);
    }

    [Fact]
    public void Compile_OrdinalOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var columns = MakeColumns("id", "name");
        var expr = FilterStar.Column(99).Gt(30);

        Assert.Throws<ArgumentOutOfRangeException>(() => FilterTreeCompiler.Compile(expr, columns));
    }

    [Fact]
    public void Compile_DeeplyNested_ExceedsMaxDepth_Throws()
    {
        var columns = MakeColumns("x");

        // Build a chain of NOT(...) 40 levels deep — exceeds MaxDepth=32
        IFilterStar expr = FilterStar.Column("x").Gt(0);
        for (int i = 0; i < 40; i++)
            expr = FilterStar.Not(expr);

        Assert.Throws<ArgumentException>(() => FilterTreeCompiler.Compile(expr, columns));
    }

    [Fact]
    public void Compile_ComplexNestedExpression_Works()
    {
        var columns = MakeColumns("age", "name", "city");

        var expr = FilterStar.And(
            FilterStar.Column("age").Between(18, 65),
            FilterStar.Or(
                FilterStar.Column("name").StartsWith("A"),
                FilterStar.Column("city").Eq("London")
            )
        );

        var node = FilterTreeCompiler.Compile(expr, columns);
        Assert.IsType<AndNode>(node);
    }

    // ─── Tier 1 vs Tier 2 parity tests ────────────────────────────────

    private static List<ColumnInfo> MakeTypedColumns(params (string name, string type)[] defs)
    {
        var list = new List<ColumnInfo>();
        for (int i = 0; i < defs.Length; i++)
            list.Add(new ColumnInfo
            {
                Name = defs[i].name,
                Ordinal = i,
                DeclaredType = defs[i].type,
                IsPrimaryKey = false,
                IsNotNull = false
            });
        return list;
    }

    [Fact]
    public void Tier1VsTier2_SimpleIntGt_SameResult()
    {
        var columns = MakeTypedColumns(("age", "INTEGER"));
        var filter = FilterStar.Column("age").Gt(30);

        var tier1 = FilterTreeCompiler.Compile(filter, columns);
        var tier2 = FilterTreeCompiler.CompileBaked(filter, columns);

        // age=42 → true
        byte[] payload = [2, 1, 42];
        long[] serialTypes = [1];
        Assert.Equal(
            tier1.Evaluate(payload, serialTypes, 2, 1),
            tier2.Evaluate(payload, serialTypes, 2, 1));

        // age=20 → false
        byte[] payload2 = [2, 1, 20];
        Assert.Equal(
            tier1.Evaluate(payload2, serialTypes, 2, 1),
            tier2.Evaluate(payload2, serialTypes, 2, 1));
    }

    [Fact]
    public void Tier1VsTier2_AndExpression_SameResult()
    {
        var columns = MakeTypedColumns(("age", "INTEGER"), ("score", "REAL"));
        var filter = FilterStar.And(
            FilterStar.Column("age").Gt(30),
            FilterStar.Column("score").Lt(50.0));

        var tier1 = FilterTreeCompiler.Compile(filter, columns);
        var tier2 = FilterTreeCompiler.CompileBaked(filter, columns);

        // age=42, score=25.5 → true,true → true
        byte[] payload = [3, 1, 7, 42, 0x40, 0x39, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00];
        long[] serialTypes = [1, 7];
        Assert.Equal(
            tier1.Evaluate(payload, serialTypes, 3, 1),
            tier2.Evaluate(payload, serialTypes, 3, 1));
    }

    [Fact]
    public void Tier1VsTier2_IsNull_SameResult()
    {
        var columns = MakeTypedColumns(("bio", "TEXT"));
        var filter = FilterStar.Column("bio").IsNull();

        var tier1 = FilterTreeCompiler.Compile(filter, columns);
        var tier2 = FilterTreeCompiler.CompileBaked(filter, columns);

        // bio=NULL (serial type 0)
        byte[] payload = [2, 0];
        long[] serialTypes = [0];
        Assert.Equal(
            tier1.Evaluate(payload, serialTypes, 2, 1),
            tier2.Evaluate(payload, serialTypes, 2, 1));
    }

    // ─── Cross-type: INTEGER column vs Double filter (Tier 1 + Tier 2 parity) ──

    [Theory]
    [InlineData(42, 42.0, true, true, false, true, false, true)]   // col==filter
    [InlineData(42, 43.0, false, false, true, true, false, false)] // col < filter
    [InlineData(42, 41.0, false, false, false, false, true, true)] // col > filter
    public void CrossType_IntColumnDoubleFilter_AllOps(
        long colVal, double filterVal,
        bool expectEq, bool expectNeq_IsFalse, bool expectLt, bool expectLte, bool expectGt, bool expectGte)
    {
        var columns = MakeTypedColumns(("val", "INTEGER"));

        // Encode column value as a 1-byte SQLite integer (serial type 1)
        // Payload: [headerSize=2, serialType=1, bodyByte]
        byte[] payload = [2, 1, (byte)colVal];
        long[] serialTypes = [1];

        // --- Eq ---
        var filter = FilterStar.Column("val").Eq(filterVal);
        var t1 = FilterTreeCompiler.Compile(filter, columns);
        var t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectEq, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Neq ---
        filter = FilterStar.Column("val").Neq(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(!expectNeq_IsFalse, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Lt ---
        filter = FilterStar.Column("val").Lt(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectLt, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Lte ---
        filter = FilterStar.Column("val").Lte(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectLte, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Gt ---
        filter = FilterStar.Column("val").Gt(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectGt, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Gte ---
        filter = FilterStar.Column("val").Gte(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectGte, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));
    }

    [Fact]
    public void CrossType_IntColumnDoubleBetween_Tier1AndTier2Match()
    {
        var columns = MakeTypedColumns(("val", "INTEGER"));

        // val=30 → Between(25.0, 35.0) should be true
        byte[] payloadIn = [2, 1, 30];
        // val=20 → Between(25.0, 35.0) should be false
        byte[] payloadOut = [2, 1, 20];
        long[] serialTypes = [1];

        var filter = FilterStar.Column("val").Between(25.0, 35.0);
        var t1 = FilterTreeCompiler.Compile(filter, columns);
        var t2 = FilterTreeCompiler.CompileBaked(filter, columns);

        Assert.True(t1.Evaluate(payloadIn, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payloadIn, serialTypes, 2, 1),
                     t2.Evaluate(payloadIn, serialTypes, 2, 1));

        Assert.False(t1.Evaluate(payloadOut, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payloadOut, serialTypes, 2, 1),
                     t2.Evaluate(payloadOut, serialTypes, 2, 1));
    }

    // ─── Cross-type: REAL column vs Int64 filter (Tier 1 + Tier 2 parity) ──

    [Theory]
    [InlineData(42.0, 42, true, true, false, true, false, true)]   // col==filter
    [InlineData(41.5, 42, false, false, true, true, false, false)] // col < filter
    [InlineData(42.5, 42, false, false, false, false, true, true)] // col > filter
    public void CrossType_RealColumnIntFilter_AllOps(
        double colVal, long filterVal,
        bool expectEq, bool expectNeq_IsFalse, bool expectLt, bool expectLte, bool expectGt, bool expectGte)
    {
        var columns = MakeTypedColumns(("val", "REAL"));

        // Encode as 8-byte big-endian double (serial type 7)
        // Payload: [headerSize=2, serialType=7, 8 bytes of double]
        byte[] payload = new byte[10];
        payload[0] = 2; // header size
        payload[1] = 7; // serial type REAL
        BitConverter.TryWriteBytes(payload.AsSpan(2), colVal);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(payload, 2, 8);
        long[] serialTypes = [7];

        // --- Eq ---
        var filter = FilterStar.Column("val").Eq(filterVal);
        var t1 = FilterTreeCompiler.Compile(filter, columns);
        var t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectEq, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Neq ---
        filter = FilterStar.Column("val").Neq(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(!expectNeq_IsFalse, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Lt ---
        filter = FilterStar.Column("val").Lt(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectLt, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Lte ---
        filter = FilterStar.Column("val").Lte(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectLte, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Gt ---
        filter = FilterStar.Column("val").Gt(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectGt, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));

        // --- Gte ---
        filter = FilterStar.Column("val").Gte(filterVal);
        t1 = FilterTreeCompiler.Compile(filter, columns);
        t2 = FilterTreeCompiler.CompileBaked(filter, columns);
        Assert.Equal(expectGte, t1.Evaluate(payload, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payload, serialTypes, 2, 1),
                     t2.Evaluate(payload, serialTypes, 2, 1));
    }

    [Fact]
    public void CrossType_RealColumnIntBetween_Tier1AndTier2Match()
    {
        var columns = MakeTypedColumns(("val", "REAL"));

        // val=30.5 → Between(25, 35) should be true
        byte[] payloadIn = new byte[10];
        payloadIn[0] = 2;
        payloadIn[1] = 7;
        BitConverter.TryWriteBytes(payloadIn.AsSpan(2), 30.5);
        if (BitConverter.IsLittleEndian) Array.Reverse(payloadIn, 2, 8);

        // val=20.0 → Between(25, 35) should be false
        byte[] payloadOut = new byte[10];
        payloadOut[0] = 2;
        payloadOut[1] = 7;
        BitConverter.TryWriteBytes(payloadOut.AsSpan(2), 20.0);
        if (BitConverter.IsLittleEndian) Array.Reverse(payloadOut, 2, 8);

        long[] serialTypes = [7];

        var filter = FilterStar.Column("val").Between(25, 35);
        var t1 = FilterTreeCompiler.Compile(filter, columns);
        var t2 = FilterTreeCompiler.CompileBaked(filter, columns);

        Assert.True(t1.Evaluate(payloadIn, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payloadIn, serialTypes, 2, 1),
                     t2.Evaluate(payloadIn, serialTypes, 2, 1));

        Assert.False(t1.Evaluate(payloadOut, serialTypes, 2, 1));
        Assert.Equal(t1.Evaluate(payloadOut, serialTypes, 2, 1),
                     t2.Evaluate(payloadOut, serialTypes, 2, 1));
    }
}
