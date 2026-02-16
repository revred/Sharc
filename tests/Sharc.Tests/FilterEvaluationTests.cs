using Sharc.Core;
using Sharc.Core.Query;
using Xunit;

namespace Sharc.Tests;

public sealed class FilterEvaluationTests
{
    // --- Integer equality ---

    [Fact]
    public void Matches_EqualIntegral_MatchingValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, 42L));
    }

    [Fact]
    public void Matches_EqualIntegral_DifferentValue_ReturnsFalse()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, 99L));
    }

    [Fact]
    public void Matches_NotEqualIntegral_DifferentValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.NotEqual, 99L));
    }

    [Fact]
    public void Matches_NotEqualIntegral_SameValue_ReturnsFalse()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.NotEqual, 42L));
    }

    // --- Integer ordering ---

    [Fact]
    public void Matches_LessThanIntegral_SmallerValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 5L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.LessThan, 10L));
    }

    [Fact]
    public void Matches_LessThanIntegral_EqualValue_ReturnsFalse()
    {
        var col = ColumnValue.FromInt64(4, 10L);
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.LessThan, 10L));
    }

    [Fact]
    public void Matches_GreaterThanIntegral_LargerValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 15L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.GreaterThan, 10L));
    }

    [Fact]
    public void Matches_LessOrEqualIntegral_EqualValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 10L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.LessOrEqual, 10L));
    }

    [Fact]
    public void Matches_GreaterOrEqualIntegral_EqualValue_ReturnsTrue()
    {
        var col = ColumnValue.FromInt64(4, 10L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.GreaterOrEqual, 10L));
    }

    // --- Int-to-long coercion ---

    [Fact]
    public void Matches_EqualIntegral_IntFilterValue_CoercesToLong()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, (int)42));
    }

    // --- Text comparisons ---

    [Fact]
    public void Matches_EqualText_MatchingString_ReturnsTrue()
    {
        var col = ColumnValue.Text(13 + 2 * 5, "hello"u8.ToArray());
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, "hello"));
    }

    [Fact]
    public void Matches_EqualText_CaseSensitive_ReturnsFalse()
    {
        var col = ColumnValue.Text(13 + 2 * 5, "Hello"u8.ToArray());
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, "hello"));
    }

    [Fact]
    public void Matches_LessThanText_AlphabeticallySmaller_ReturnsTrue()
    {
        var col = ColumnValue.Text(13 + 2 * 3, "abc"u8.ToArray());
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.LessThan, "def"));
    }

    // --- Real comparisons ---

    [Fact]
    public void Matches_EqualReal_MatchingDouble_ReturnsTrue()
    {
        var col = ColumnValue.FromDouble(3.14);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, 3.14));
    }

    [Fact]
    public void Matches_GreaterThanReal_LargerValue_ReturnsTrue()
    {
        var col = ColumnValue.FromDouble(3.14);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.GreaterThan, 2.0));
    }

    // --- Cross-type: integral column vs double filter ---

    [Fact]
    public void Matches_EqualIntegral_DoubleFilterValue_CrossTypeComparison()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, 42.0));
    }

    // --- NULL handling ---

    [Fact]
    public void Matches_NullColumn_AnyOperator_ReturnsFalse()
    {
        var col = ColumnValue.Null();
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, 42L));
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.NotEqual, 42L));
    }

    [Fact]
    public void Matches_NullFilterValue_NonNullColumn_ReturnsFalse()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, null));
    }

    // --- MatchesAll ---

    [Fact]
    public void MatchesAll_AllFiltersMatch_ReturnsTrue()
    {
        var row = new[]
        {
            ColumnValue.FromInt64(4, 25L),
            ColumnValue.Text(13 + 2 * 5, "Alice"u8.ToArray())
        };
        var filters = new[]
        {
            new ResolvedFilter { ColumnOrdinal = 0, Operator = SharcOperator.GreaterOrEqual, Value = 20L },
            new ResolvedFilter { ColumnOrdinal = 1, Operator = SharcOperator.Equal, Value = "Alice" }
        };

        Assert.True(FilterEvaluator.MatchesAll(filters, row));
    }

    [Fact]
    public void MatchesAll_OneFilterFails_ReturnsFalse()
    {
        var row = new[]
        {
            ColumnValue.FromInt64(4, 25L),
            ColumnValue.Text(13 + 2 * 3, "Bob"u8.ToArray())
        };
        var filters = new[]
        {
            new ResolvedFilter { ColumnOrdinal = 0, Operator = SharcOperator.GreaterOrEqual, Value = 20L },
            new ResolvedFilter { ColumnOrdinal = 1, Operator = SharcOperator.Equal, Value = "Alice" }
        };

        Assert.False(FilterEvaluator.MatchesAll(filters, row));
    }

    [Fact]
    public void MatchesAll_EmptyFilters_ReturnsTrue()
    {
        var row = new[] { ColumnValue.FromInt64(4, 42L) };
        Assert.True(FilterEvaluator.MatchesAll([], row));
    }

    // --- Blob column always false (not comparable) ---

    [Fact]
    public void Matches_BlobColumn_AnyOperator_ReturnsFalse()
    {
        var col = ColumnValue.Blob(14, new byte[] { 0xFF });
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, new byte[] { 0xFF }));
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.NotEqual, 1L));
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.LessThan, "abc"));
    }

    // --- Real ←→ Int cross-type ---

    [Fact]
    public void Matches_RealColumn_LongFilterValue_CrossTypeComparison()
    {
        var col = ColumnValue.FromDouble(42.0);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, 42L));
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.GreaterThan, 41L));
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.LessThan, 42L));
    }

    [Fact]
    public void Matches_RealColumn_FloatFilterValue_CrossTypeComparison()
    {
        // Use a value that round-trips exactly between float and double
        var col = ColumnValue.FromDouble(3.0);
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.Equal, 3.0f));
        Assert.True(FilterEvaluator.Matches(col, SharcOperator.GreaterOrEqual, 3.0f));
    }

    // --- Incompatible types ---

    [Fact]
    public void Matches_IntegralColumn_IncompatibleStringFilter_ReturnsFalse()
    {
        var col = ColumnValue.FromInt64(4, 42L);
        Assert.False(FilterEvaluator.Matches(col, SharcOperator.Equal, "42"));
    }
}
