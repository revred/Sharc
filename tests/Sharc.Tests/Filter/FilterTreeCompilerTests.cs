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
}
