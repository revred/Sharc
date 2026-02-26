// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Builds filter expressions using a fluent, composable API.
/// Expressions are compiled into an optimized IFilterNode tree at reader creation.
/// </summary>
public static class FilterStar
{
    /// <summary>Creates a column reference by name for building typed predicates.</summary>
    public static ColumnRef Column(string name) => new(name, null);

    /// <summary>Creates a column reference by ordinal for building typed predicates.</summary>
    public static ColumnRef Column(int ordinal) => new(null, ordinal);

    /// <summary>Combines conditions with AND semantics (all must match).</summary>
    public static IFilterStar And(params IFilterStar[] conditions) => new AndExpression(conditions);

    /// <summary>Combines conditions with OR semantics (any must match).</summary>
    public static IFilterStar Or(params IFilterStar[] conditions) => new OrExpression(conditions);

    /// <summary>Negates a condition.</summary>
    public static IFilterStar Not(IFilterStar condition) => new NotExpression(condition);
}

/// <summary>
/// Represents a column reference for building typed predicates.
/// </summary>
public readonly struct ColumnRef
{
    private readonly string? _name;
    private readonly int? _ordinal;

    internal ColumnRef(string? name, int? ordinal)
    {
        _name = name;
        _ordinal = ordinal;
    }

    private PredicateExpression Predicate(FilterOp op, TypedFilterValue value) =>
        new(_name, _ordinal, op, value);

    // â”€â”€ Comparison (long) â”€â”€

    /// <summary>Column value equals the given integer.</summary>
    public IFilterStar Eq(long value) => Predicate(FilterOp.Eq, TypedFilterValue.FromInt64(value));

    /// <summary>Column value does not equal the given integer.</summary>
    public IFilterStar Neq(long value) => Predicate(FilterOp.Neq, TypedFilterValue.FromInt64(value));

    /// <summary>Column value is less than the given integer.</summary>
    public IFilterStar Lt(long value) => Predicate(FilterOp.Lt, TypedFilterValue.FromInt64(value));

    /// <summary>Column value is less than or equal to the given integer.</summary>
    public IFilterStar Lte(long value) => Predicate(FilterOp.Lte, TypedFilterValue.FromInt64(value));

    /// <summary>Column value is greater than the given integer.</summary>
    public IFilterStar Gt(long value) => Predicate(FilterOp.Gt, TypedFilterValue.FromInt64(value));

    /// <summary>Column value is greater than or equal to the given integer.</summary>
    public IFilterStar Gte(long value) => Predicate(FilterOp.Gte, TypedFilterValue.FromInt64(value));

    /// <summary>Column value is between low and high (inclusive).</summary>
    public IFilterStar Between(long low, long high) =>
        Predicate(FilterOp.Between, TypedFilterValue.FromInt64Range(low, high));

    // â”€â”€ Comparison (double) â”€â”€

    /// <summary>Column value equals the given double.</summary>
    public IFilterStar Eq(double value) => Predicate(FilterOp.Eq, TypedFilterValue.FromDouble(value));

    /// <summary>Column value does not equal the given double.</summary>
    public IFilterStar Neq(double value) => Predicate(FilterOp.Neq, TypedFilterValue.FromDouble(value));

    /// <summary>Column value is less than the given double.</summary>
    public IFilterStar Lt(double value) => Predicate(FilterOp.Lt, TypedFilterValue.FromDouble(value));

    /// <summary>Column value is less than or equal to the given double.</summary>
    public IFilterStar Lte(double value) => Predicate(FilterOp.Lte, TypedFilterValue.FromDouble(value));

    /// <summary>Column value is greater than the given double.</summary>
    public IFilterStar Gt(double value) => Predicate(FilterOp.Gt, TypedFilterValue.FromDouble(value));

    /// <summary>Column value is greater than or equal to the given double.</summary>
    public IFilterStar Gte(double value) => Predicate(FilterOp.Gte, TypedFilterValue.FromDouble(value));

    /// <summary>Column value is between low and high (inclusive).</summary>
    public IFilterStar Between(double low, double high) =>
        Predicate(FilterOp.Between, TypedFilterValue.FromDoubleRange(low, high));

    // â”€â”€ Comparison (string) â”€â”€

    // Comparison (decimal)

    /// <summary>Column value equals the given decimal (canonical exact match).</summary>
    public IFilterStar Eq(decimal value) => Predicate(FilterOp.Eq, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value does not equal the given decimal (canonical exact match).</summary>
    public IFilterStar Neq(decimal value) => Predicate(FilterOp.Neq, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value is less than the given decimal.</summary>
    public IFilterStar Lt(decimal value) => Predicate(FilterOp.Lt, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value is less than or equal to the given decimal.</summary>
    public IFilterStar Lte(decimal value) => Predicate(FilterOp.Lte, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value is greater than the given decimal.</summary>
    public IFilterStar Gt(decimal value) => Predicate(FilterOp.Gt, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value is greater than or equal to the given decimal.</summary>
    public IFilterStar Gte(decimal value) => Predicate(FilterOp.Gte, TypedFilterValue.FromDecimal(value));

    /// <summary>Column value equals the given string (ordinal UTF-8 comparison).</summary>
    public IFilterStar Eq(string value) => Predicate(FilterOp.Eq, TypedFilterValue.FromUtf8(value));

    /// <summary>Column value does not equal the given string.</summary>
    public IFilterStar Neq(string value) => Predicate(FilterOp.Neq, TypedFilterValue.FromUtf8(value));

    /// <summary>Column value is less than the given string (ordinal UTF-8).</summary>
    public IFilterStar Lt(string value) => Predicate(FilterOp.Lt, TypedFilterValue.FromUtf8(value));

    /// <summary>Column value is less than or equal to the given string.</summary>
    public IFilterStar Lte(string value) => Predicate(FilterOp.Lte, TypedFilterValue.FromUtf8(value));

    /// <summary>Column value is greater than the given string.</summary>
    public IFilterStar Gt(string value) => Predicate(FilterOp.Gt, TypedFilterValue.FromUtf8(value));

    /// <summary>Column value is greater than or equal to the given string.</summary>
    public IFilterStar Gte(string value) => Predicate(FilterOp.Gte, TypedFilterValue.FromUtf8(value));

    // â”€â”€ Comparison (GUID) â”€â”€

    /// <summary>Column value matches the given GUID.</summary>
    public IFilterStar Eq(Guid value) => Predicate(FilterOp.Eq, TypedFilterValue.FromGuid(value));

    /// <summary>Column value does not match the given GUID.</summary>
    public IFilterStar Neq(Guid value) => Predicate(FilterOp.Neq, TypedFilterValue.FromGuid(value));

    // â”€â”€ Null â”€â”€

    /// <summary>Column value is NULL.</summary>
    public IFilterStar IsNull() => Predicate(FilterOp.IsNull, TypedFilterValue.FromNull());

    /// <summary>Column value is not NULL.</summary>
    public IFilterStar IsNotNull() => Predicate(FilterOp.IsNotNull, TypedFilterValue.FromNull());

    // â”€â”€ String operations â”€â”€

    /// <summary>Column text starts with the given UTF-8 prefix.</summary>
    public IFilterStar StartsWith(string prefix) =>
        Predicate(FilterOp.StartsWith, TypedFilterValue.FromUtf8(prefix));

    /// <summary>Column text ends with the given UTF-8 suffix.</summary>
    public IFilterStar EndsWith(string suffix) =>
        Predicate(FilterOp.EndsWith, TypedFilterValue.FromUtf8(suffix));

    /// <summary>Column text contains the given UTF-8 substring.</summary>
    public IFilterStar Contains(string substring) =>
        Predicate(FilterOp.Contains, TypedFilterValue.FromUtf8(substring));

    // â”€â”€ Set membership â”€â”€

    /// <summary>Column integer value is in the given set.</summary>
    public IFilterStar In(params long[] values) =>
        Predicate(FilterOp.In, TypedFilterValue.FromInt64Set(values));

    /// <summary>Column text value is in the given set.</summary>
    public IFilterStar In(params string[] values) =>
        Predicate(FilterOp.In, TypedFilterValue.FromUtf8Set(values));

    /// <summary>Column integer value is not in the given set.</summary>
    public IFilterStar NotIn(params long[] values) =>
        Predicate(FilterOp.NotIn, TypedFilterValue.FromInt64Set(values));

    /// <summary>Column text value is not in the given set.</summary>
    public IFilterStar NotIn(params string[] values) =>
        Predicate(FilterOp.NotIn, TypedFilterValue.FromUtf8Set(values));
}
