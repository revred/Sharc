// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sharc.Query;

/// <summary>
/// Storage class tag for <see cref="QueryValue"/>.
/// </summary>
internal enum QueryValueType : byte
{
    Null = 0,
    Int64 = 1,
    Double = 2,
    Text = 3,
    Blob = 4,
}

/// <summary>
/// Unboxed query pipeline value. Stores <c>long</c> and <c>double</c> inline
/// (no heap allocation), while <c>string</c> and <c>byte[]</c> use a managed reference.
/// Replaces <c>object?</c> in the materialization pipeline to eliminate boxing.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct QueryValue
{
    private long _numericValue;
    internal object? ObjectValue;
    internal QueryValueType Type;

    /// <summary>Creates a NULL query value.</summary>
    public static QueryValue Null => new() { Type = QueryValueType.Null };

    /// <summary>Creates an integer query value (stored inline, no boxing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QueryValue FromInt64(long value) =>
        new() { _numericValue = value, Type = QueryValueType.Int64 };

    /// <summary>Creates a double query value (stored inline, no boxing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QueryValue FromDouble(double value)
    {
        var qv = new QueryValue { Type = QueryValueType.Double };
        Unsafe.As<long, double>(ref qv._numericValue) = value;
        return qv;
    }

    /// <summary>Creates a text query value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QueryValue FromString(string value) =>
        new() { ObjectValue = value, Type = QueryValueType.Text };

    /// <summary>Creates a blob query value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QueryValue FromBlob(byte[] value) =>
        new() { ObjectValue = value, Type = QueryValueType.Blob };

    /// <summary>Gets the integer value. Only valid when Type is Int64.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly long AsInt64() => _numericValue;

    /// <summary>Gets the double value. Only valid when Type is Double.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double AsDouble() => Unsafe.As<long, double>(ref Unsafe.AsRef(in _numericValue));

    /// <summary>Gets the string value. Only valid when Type is Text.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly string AsString() => (string)ObjectValue!;

    /// <summary>Gets the blob value. Only valid when Type is Blob.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte[] AsBlob() => (byte[])ObjectValue!;

    /// <summary>Returns true if this value is NULL.</summary>
    public readonly bool IsNull => Type == QueryValueType.Null;

    /// <summary>
    /// Converts this value to a boxed <c>object</c> for the public API boundary.
    /// This is the ONLY place boxing occurs — called when the user invokes
    /// <see cref="SharcDataReader.GetValue(int)"/>.
    /// </summary>
    public readonly object ToObject() => Type switch
    {
        QueryValueType.Int64 => AsInt64(),
        QueryValueType.Double => AsDouble(),
        QueryValueType.Text => AsString(),
        QueryValueType.Blob => AsBlob(),
        _ => DBNull.Value,
    };
}
