using Sharc;
using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Tests.Filter;

public sealed class FilterCompilerTests
{
    private static IReadOnlyList<ColumnInfo> MakeColumns(params string[] names)
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

        var node = FilterCompiler.Compile(expr, columns);

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

        var node = FilterCompiler.Compile(expr, columns);

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

        var node = FilterCompiler.Compile(expr, columns);

        Assert.IsType<OrNode>(node);
    }

    [Fact]
    public void Compile_NotExpression_ReturnsNotNode()
    {
        var columns = MakeColumns("deleted");
        var expr = FilterStar.Not(FilterStar.Column("deleted").IsNotNull());

        var node = FilterCompiler.Compile(expr, columns);

        Assert.IsType<NotNode>(node);
    }

    [Fact]
    public void Compile_ColumnNameResolution_CaseInsensitive()
    {
        var columns = MakeColumns("AGE");
        var expr = FilterStar.Column("age").Gt(30);

        // Should not throw — case-insensitive match
        var node = FilterCompiler.Compile(expr, columns);
        Assert.NotNull(node);
    }

    [Fact]
    public void Compile_InvalidColumnName_ThrowsArgumentException()
    {
        var columns = MakeColumns("id", "name");
        var expr = FilterStar.Column("nonexistent").Gt(30);

        Assert.Throws<ArgumentException>(() => FilterCompiler.Compile(expr, columns));
    }

    [Fact]
    public void Compile_ColumnByOrdinal_Works()
    {
        var columns = MakeColumns("id", "name", "age");
        var expr = FilterStar.Column(2).Gt(30);

        var node = FilterCompiler.Compile(expr, columns);
        Assert.IsType<PredicateNode>(node);
    }

    [Fact]
    public void Compile_OrdinalOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var columns = MakeColumns("id", "name");
        var expr = FilterStar.Column(99).Gt(30);

        Assert.Throws<ArgumentOutOfRangeException>(() => FilterCompiler.Compile(expr, columns));
    }

    [Fact]
    public void Compile_DeeplyNested_ExceedsMaxDepth_Throws()
    {
        var columns = MakeColumns("x");

        // Build a chain of NOT(...) 40 levels deep — exceeds MaxDepth=32
        IFilterStar expr = FilterStar.Column("x").Gt(0);
        for (int i = 0; i < 40; i++)
            expr = FilterStar.Not(expr);

        Assert.Throws<ArgumentException>(() => FilterCompiler.Compile(expr, columns));
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

        var node = FilterCompiler.Compile(expr, columns);
        Assert.IsType<AndNode>(node);
    }
}
