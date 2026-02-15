// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

namespace Sharc.Query.Intent;

/// <summary>Discriminant for <see cref="IntentValue"/>.</summary>
public enum IntentValueKind : byte
{
    /// <summary>SQL NULL.</summary>
    Null,
    /// <summary>64-bit signed integer.</summary>
    Signed64,
    /// <summary>64-bit floating point.</summary>
    Real,
    /// <summary>UTF-16 text.</summary>
    Text,
    /// <summary>Boolean.</summary>
    Bool,
    /// <summary>Named parameter ($name).</summary>
    Parameter,
    /// <summary>Set of 64-bit integers (for IN).</summary>
    Signed64Set,
    /// <summary>Set of text values (for IN).</summary>
    TextSet,
}

/// <summary>
/// Inline-storage value for intent predicates. No boxing for primitives.
/// </summary>
public readonly struct IntentValue
{
    /// <summary>The kind of value stored.</summary>
    public IntentValueKind Kind { get; }

    private readonly long _int64;
    private readonly double _float64;
    private readonly string? _text;
    private readonly long[]? _int64Set;
    private readonly string[]? _textSet;

    private IntentValue(IntentValueKind kind, long i64 = 0, double f64 = 0,
        string? text = null, long[]? i64Set = null, string[]? textSet = null)
    {
        Kind = kind;
        _int64 = i64;
        _float64 = f64;
        _text = text;
        _int64Set = i64Set;
        _textSet = textSet;
    }

    // ─── Accessors ──────────────────────────────────────────────

    /// <summary>Returns the stored 64-bit integer.</summary>
    public long AsInt64 => _int64;
    /// <summary>Returns the stored 64-bit float.</summary>
    public double AsFloat64 => _float64;
    /// <summary>Returns the stored text or parameter name.</summary>
    public string? AsText => _text;
    /// <summary>Returns the stored boolean.</summary>
    public bool AsBool => _int64 != 0;
    /// <summary>Returns the stored integer set.</summary>
    public long[]? AsInt64Set => _int64Set;
    /// <summary>Returns the stored text set.</summary>
    public string[]? AsTextSet => _textSet;

    // ─── Factories ──────────────────────────────────────────────

    /// <summary>SQL NULL value.</summary>
    public static IntentValue Null => default;

    /// <summary>Creates an integer value.</summary>
    public static IntentValue FromInt64(long value) =>
        new(IntentValueKind.Signed64, i64: value);

    /// <summary>Creates a floating-point value.</summary>
    public static IntentValue FromFloat64(double value) =>
        new(IntentValueKind.Real, f64: value);

    /// <summary>Creates a text value.</summary>
    public static IntentValue FromText(string value) =>
        new(IntentValueKind.Text, text: value);

    /// <summary>Creates a boolean value.</summary>
    public static IntentValue FromBool(bool value) =>
        new(IntentValueKind.Bool, i64: value ? 1L : 0L);

    /// <summary>Creates a named parameter reference.</summary>
    public static IntentValue FromParameter(string name) =>
        new(IntentValueKind.Parameter, text: name);

    /// <summary>Creates an integer set (for IN operator).</summary>
    public static IntentValue FromInt64Set(long[] values) =>
        new(IntentValueKind.Signed64Set, i64Set: values);

    /// <summary>Creates a text set (for IN operator).</summary>
    public static IntentValue FromTextSet(string[] values) =>
        new(IntentValueKind.TextSet, textSet: values);

    // ─── Display ────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        IntentValueKind.Null => "NULL",
        IntentValueKind.Signed64 => _int64.ToString(CultureInfo.InvariantCulture),
        IntentValueKind.Real => _float64.ToString(CultureInfo.InvariantCulture),
        IntentValueKind.Text => $"'{_text}'",
        IntentValueKind.Bool => _int64 != 0 ? "TRUE" : "FALSE",
        IntentValueKind.Parameter => $"${_text}",
        IntentValueKind.Signed64Set => $"[{_int64Set?.Length ?? 0} values]",
        IntentValueKind.TextSet => $"[{_textSet?.Length ?? 0} values]",
        _ => "?",
    };
}
