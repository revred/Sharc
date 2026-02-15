// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class QueryValueTests
{
    // ─── Int64 ────────────────────────────────────────────────────

    [Fact]
    public void FromInt64_RoundTrips()
    {
        var qv = QueryValue.FromInt64(42L);
        Assert.Equal(QueryValueType.Int64, qv.Type);
        Assert.Equal(42L, qv.AsInt64());
        Assert.False(qv.IsNull);
    }

    [Fact]
    public void FromInt64_NegativeValue_RoundTrips()
    {
        var qv = QueryValue.FromInt64(-9999L);
        Assert.Equal(-9999L, qv.AsInt64());
    }

    [Fact]
    public void FromInt64_Zero_RoundTrips()
    {
        var qv = QueryValue.FromInt64(0L);
        Assert.Equal(0L, qv.AsInt64());
    }

    [Fact]
    public void FromInt64_MaxValue_RoundTrips()
    {
        var qv = QueryValue.FromInt64(long.MaxValue);
        Assert.Equal(long.MaxValue, qv.AsInt64());
    }

    [Fact]
    public void FromInt64_MinValue_RoundTrips()
    {
        var qv = QueryValue.FromInt64(long.MinValue);
        Assert.Equal(long.MinValue, qv.AsInt64());
    }

    // ─── Double ───────────────────────────────────────────────────

    [Fact]
    public void FromDouble_RoundTrips()
    {
        var qv = QueryValue.FromDouble(3.14);
        Assert.Equal(QueryValueType.Double, qv.Type);
        Assert.Equal(3.14, qv.AsDouble());
        Assert.False(qv.IsNull);
    }

    [Fact]
    public void FromDouble_NegativeValue_RoundTrips()
    {
        var qv = QueryValue.FromDouble(-273.15);
        Assert.Equal(-273.15, qv.AsDouble());
    }

    [Fact]
    public void FromDouble_Zero_RoundTrips()
    {
        var qv = QueryValue.FromDouble(0.0);
        Assert.Equal(0.0, qv.AsDouble());
    }

    [Fact]
    public void FromDouble_MaxValue_RoundTrips()
    {
        var qv = QueryValue.FromDouble(double.MaxValue);
        Assert.Equal(double.MaxValue, qv.AsDouble());
    }

    // ─── String ───────────────────────────────────────────────────

    [Fact]
    public void FromString_RoundTrips()
    {
        var qv = QueryValue.FromString("hello");
        Assert.Equal(QueryValueType.Text, qv.Type);
        Assert.Equal("hello", qv.AsString());
        Assert.False(qv.IsNull);
    }

    [Fact]
    public void FromString_EmptyString_RoundTrips()
    {
        var qv = QueryValue.FromString("");
        Assert.Equal("", qv.AsString());
    }

    // ─── Blob ─────────────────────────────────────────────────────

    [Fact]
    public void FromBlob_RoundTrips()
    {
        byte[] data = [1, 2, 3, 4];
        var qv = QueryValue.FromBlob(data);
        Assert.Equal(QueryValueType.Blob, qv.Type);
        Assert.Same(data, qv.AsBlob());
        Assert.False(qv.IsNull);
    }

    // ─── Null ─────────────────────────────────────────────────────

    [Fact]
    public void Null_IsNull()
    {
        var qv = QueryValue.Null;
        Assert.Equal(QueryValueType.Null, qv.Type);
        Assert.True(qv.IsNull);
    }

    [Fact]
    public void Default_IsNull()
    {
        QueryValue qv = default;
        Assert.True(qv.IsNull);
    }

    // ─── ToObject (boundary boxing) ──────────────────────────────

    [Fact]
    public void ToObject_Int64_BoxesCorrectly()
    {
        var qv = QueryValue.FromInt64(42L);
        object obj = qv.ToObject();
        Assert.IsType<long>(obj);
        Assert.Equal(42L, (long)obj);
    }

    [Fact]
    public void ToObject_Double_BoxesCorrectly()
    {
        var qv = QueryValue.FromDouble(3.14);
        object obj = qv.ToObject();
        Assert.IsType<double>(obj);
        Assert.Equal(3.14, (double)obj);
    }

    [Fact]
    public void ToObject_String_ReturnsString()
    {
        var qv = QueryValue.FromString("test");
        object obj = qv.ToObject();
        Assert.IsType<string>(obj);
        Assert.Equal("test", (string)obj);
    }

    [Fact]
    public void ToObject_Blob_ReturnsByteArray()
    {
        byte[] data = [5, 6, 7];
        var qv = QueryValue.FromBlob(data);
        object obj = qv.ToObject();
        Assert.IsType<byte[]>(obj);
        Assert.Same(data, obj);
    }

    [Fact]
    public void ToObject_Null_ReturnsDBNull()
    {
        var qv = QueryValue.Null;
        Assert.Same(DBNull.Value, qv.ToObject());
    }

    // ─── Struct size verification ────────────────────────────────

    [Fact]
    public void QueryValue_IsValueType()
    {
        Assert.True(typeof(QueryValue).IsValueType);
    }

    [Fact]
    public void QueryValue_SizeIsCompact()
    {
        // QueryValue should be <= 24 bytes (long + object ref + byte + padding)
        // On 64-bit: 8 (long) + 8 (object ref) + 1 (enum) + 7 (padding) = 24
        int size = Unsafe.SizeOf<QueryValue>();
        Assert.True(size <= 24, $"QueryValue size is {size} bytes, expected <= 24");
    }
}
