using Xunit;

namespace Sharc.Tests.Filter;

public sealed class FilterStarTests
{
    [Fact]
    public void Column_ByName_ReturnsIFilterStar()
    {
        IFilterStar expr = FilterStar.Column("age").Gt(30);
        Assert.NotNull(expr);
        Assert.IsType<PredicateExpression>(expr);
    }

    [Fact]
    public void Column_ByOrdinal_ReturnsIFilterStar()
    {
        IFilterStar expr = FilterStar.Column(2).Gt(30);
        Assert.NotNull(expr);
        var pred = Assert.IsType<PredicateExpression>(expr);
        Assert.Equal(2, pred.ColumnOrdinal);
    }

    [Fact]
    public void And_CombinesMultipleConditions()
    {
        var expr = FilterStar.And(
            FilterStar.Column("age").Gt(30),
            FilterStar.Column("score").Lt(50)
        );

        var and = Assert.IsType<AndExpression>(expr);
        Assert.Equal(2, and.Children.Length);
    }

    [Fact]
    public void Or_CombinesMultipleConditions()
    {
        var expr = FilterStar.Or(
            FilterStar.Column("status").Eq("active"),
            FilterStar.Column("status").Eq("pending")
        );

        var or = Assert.IsType<OrExpression>(expr);
        Assert.Equal(2, or.Children.Length);
    }

    [Fact]
    public void Not_NegatesCondition()
    {
        var expr = FilterStar.Not(FilterStar.Column("deleted").IsNotNull());
        var not = Assert.IsType<NotExpression>(expr);
        Assert.IsType<PredicateExpression>(not.Inner);
    }

    [Fact]
    public void Column_AllComparisonOperators_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("x");

        Assert.IsType<PredicateExpression>(col.Eq(1L));
        Assert.IsType<PredicateExpression>(col.Neq(1L));
        Assert.IsType<PredicateExpression>(col.Lt(1L));
        Assert.IsType<PredicateExpression>(col.Lte(1L));
        Assert.IsType<PredicateExpression>(col.Gt(1L));
        Assert.IsType<PredicateExpression>(col.Gte(1L));
        Assert.IsType<PredicateExpression>(col.Between(1L, 10L));
    }

    [Fact]
    public void Column_NullOperators_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("x");

        var isNull = Assert.IsType<PredicateExpression>(col.IsNull());
        Assert.Equal(FilterOp.IsNull, isNull.Operator);

        var isNotNull = Assert.IsType<PredicateExpression>(col.IsNotNull());
        Assert.Equal(FilterOp.IsNotNull, isNotNull.Operator);
    }

    [Fact]
    public void Column_StringOperators_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("name");

        Assert.IsType<PredicateExpression>(col.StartsWith("A"));
        Assert.IsType<PredicateExpression>(col.EndsWith("z"));
        Assert.IsType<PredicateExpression>(col.Contains("mid"));
    }

    [Fact]
    public void Column_SetOperators_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("status");

        var inInt = Assert.IsType<PredicateExpression>(col.In(1L, 2L, 3L));
        Assert.Equal(FilterOp.In, inInt.Operator);

        var inStr = Assert.IsType<PredicateExpression>(col.In("a", "b", "c"));
        Assert.Equal(FilterOp.In, inStr.Operator);

        Assert.IsType<PredicateExpression>(col.NotIn(1L, 2L));
        Assert.IsType<PredicateExpression>(col.NotIn("x", "y"));
    }

    [Fact]
    public void ComplexNested_Compiles()
    {
        // (age BETWEEN 18 AND 65) AND (name STARTS WITH 'A' OR city = 'London')
        var expr = FilterStar.And(
            FilterStar.Column("age").Between(18, 65),
            FilterStar.Or(
                FilterStar.Column("name").StartsWith("A"),
                FilterStar.Column("city").Eq("London")
            )
        );

        var and = Assert.IsType<AndExpression>(expr);
        Assert.Equal(2, and.Children.Length);
        Assert.IsType<PredicateExpression>(and.Children[0]);
        Assert.IsType<OrExpression>(and.Children[1]);
    }

    [Fact]
    public void Column_DoubleOverloads_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("balance");

        Assert.IsType<PredicateExpression>(col.Eq(3.14));
        Assert.IsType<PredicateExpression>(col.Lt(3.14));
        Assert.IsType<PredicateExpression>(col.Between(1.0, 10.0));
    }

    [Fact]
    public void Column_StringEqNeq_CreatePredicateExpressions()
    {
        var col = FilterStar.Column("name");

        var eq = Assert.IsType<PredicateExpression>(col.Eq("Alice"));
        Assert.Equal(FilterOp.Eq, eq.Operator);

        var neq = Assert.IsType<PredicateExpression>(col.Neq("Bob"));
        Assert.Equal(FilterOp.Neq, neq.Operator);
    }
}
