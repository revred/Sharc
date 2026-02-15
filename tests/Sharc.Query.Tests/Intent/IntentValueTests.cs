// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using Xunit;

namespace Sharc.Query.Tests.Intent;

public class IntentValueTests
{
    // ─── Factory + Kind ─────────────────────────────────────────────

    [Fact]
    public void Null_Kind_IsNull()
    {
        var v = IntentValue.Null;
        Assert.Equal(IntentValueKind.Null, v.Kind);
    }

    [Fact]
    public void FromInt64_StoresValue()
    {
        var v = IntentValue.FromInt64(42L);
        Assert.Equal(IntentValueKind.Signed64, v.Kind);
        Assert.Equal(42L, v.AsInt64);
    }

    [Fact]
    public void FromFloat64_StoresValue()
    {
        var v = IntentValue.FromFloat64(3.14);
        Assert.Equal(IntentValueKind.Real, v.Kind);
        Assert.Equal(3.14, v.AsFloat64);
    }

    [Fact]
    public void FromText_StoresValue()
    {
        var v = IntentValue.FromText("hello");
        Assert.Equal(IntentValueKind.Text, v.Kind);
        Assert.Equal("hello", v.AsText);
    }

    [Fact]
    public void FromBool_True_StoresValue()
    {
        var v = IntentValue.FromBool(true);
        Assert.Equal(IntentValueKind.Bool, v.Kind);
        Assert.True(v.AsBool);
    }

    [Fact]
    public void FromBool_False_StoresValue()
    {
        var v = IntentValue.FromBool(false);
        Assert.Equal(IntentValueKind.Bool, v.Kind);
        Assert.False(v.AsBool);
    }

    [Fact]
    public void FromParameter_StoresName()
    {
        var v = IntentValue.FromParameter("userId");
        Assert.Equal(IntentValueKind.Parameter, v.Kind);
        Assert.Equal("userId", v.AsText);
    }

    [Fact]
    public void FromInt64Set_StoresValues()
    {
        long[] set = [1L, 2L, 3L];
        var v = IntentValue.FromInt64Set(set);
        Assert.Equal(IntentValueKind.Signed64Set, v.Kind);
        Assert.Equal(set, v.AsInt64Set);
    }

    [Fact]
    public void FromTextSet_StoresValues()
    {
        string[] set = ["a", "b", "c"];
        var v = IntentValue.FromTextSet(set);
        Assert.Equal(IntentValueKind.TextSet, v.Kind);
        Assert.Equal(set, v.AsTextSet);
    }

    // ─── Default value ──────────────────────────────────────────────

    [Fact]
    public void Default_IsNull()
    {
        IntentValue v = default;
        Assert.Equal(IntentValueKind.Null, v.Kind);
    }

    // ─── ToString ───────────────────────────────────────────────────

    [Fact]
    public void ToString_Null_ReturnsNull()
    {
        Assert.Equal("NULL", IntentValue.Null.ToString());
    }

    [Fact]
    public void ToString_Int64_ReturnsNumber()
    {
        Assert.Equal("42", IntentValue.FromInt64(42L).ToString());
    }

    [Fact]
    public void ToString_Text_ReturnsQuoted()
    {
        Assert.Equal("'hello'", IntentValue.FromText("hello").ToString());
    }

    [Fact]
    public void ToString_Parameter_ReturnsDollar()
    {
        Assert.Equal("$userId", IntentValue.FromParameter("userId").ToString());
    }
}
