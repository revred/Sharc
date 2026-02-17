/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for ColumnValue struct.
/// </summary>
public class ColumnValueTests
{
    [Fact]
    public void Null_IsNullReturnsTrue()
    {
        var val = ColumnValue.Null();
        Assert.True(val.IsNull);
        Assert.Equal(ColumnStorageClass.Null, val.StorageClass);
    }

    [Fact]
    public void Integer_StoresAndRetrievesValue()
    {
        var val = ColumnValue.FromInt64(4, 42);
        Assert.False(val.IsNull);
        Assert.Equal(ColumnStorageClass.Integral, val.StorageClass);
        Assert.Equal(42L, val.AsInt64());
    }

    [Fact]
    public void Integer_NegativeValue_Works()
    {
        var val = ColumnValue.FromInt64(6, -9999);
        Assert.Equal(-9999L, val.AsInt64());
    }

    [Fact]
    public void Float_StoresAndRetrievesValue()
    {
        var val = ColumnValue.FromDouble(3.14159);
        Assert.Equal(ColumnStorageClass.Real, val.StorageClass);
        Assert.Equal(3.14159, val.AsDouble(), 5);
    }

    [Fact]
    public void Text_StoresAndRetrievesString()
    {
        var bytes = "Hello, Sharc!"u8.ToArray();
        var val = ColumnValue.Text(39, bytes); // serial type for 13-char text: (13*2)+13 = 39
        Assert.Equal(ColumnStorageClass.Text, val.StorageClass);
        Assert.Equal("Hello, Sharc!", val.AsString());
    }

    [Fact]
    public void Blob_StoresAndRetrievesBytes()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        var val = ColumnValue.Blob(20, data); // serial type for 4-byte blob: (4*2)+12 = 20
        Assert.Equal(ColumnStorageClass.Blob, val.StorageClass);
        Assert.True(val.AsBytes().Span.SequenceEqual(data));
    }

    [Fact]
    public void Integer_ConstantZero_SerialType8()
    {
        var val = ColumnValue.FromInt64(8, 0);
        Assert.Equal(8L, val.SerialType);
        Assert.Equal(0L, val.AsInt64());
    }

    [Fact]
    public void Integer_ConstantOne_SerialType9()
    {
        var val = ColumnValue.FromInt64(9, 1);
        Assert.Equal(9L, val.SerialType);
        Assert.Equal(1L, val.AsInt64());
    }

    // --- GUID ---

    [Fact]
    public void Guid_StoresAndRetrievesValue()
    {
        var guid = Guid.NewGuid();
        var val = ColumnValue.FromGuid(guid);
        Assert.False(val.IsNull);
        Assert.Equal(ColumnStorageClass.UniqueId, val.StorageClass);
        Assert.Equal(guid, val.AsGuid());
    }

    [Fact]
    public void Guid_SerialType_Is44()
    {
        var val = ColumnValue.FromGuid(Guid.NewGuid());
        Assert.Equal(44L, val.SerialType);
    }

    [Fact]
    public void Guid_EmptyGuid_RoundTrips()
    {
        var val = ColumnValue.FromGuid(Guid.Empty);
        Assert.Equal(Guid.Empty, val.AsGuid());
    }

    [Fact]
    public void Guid_AsBytes_Returns16Bytes()
    {
        var val = ColumnValue.FromGuid(Guid.NewGuid());
        Assert.Equal(16, val.AsBytes().Length);
    }

    // --- SplitGuidForMerge ---

    [Fact]
    public void SplitGuidForMerge_KnownGuid_MatchesToInt64Pair()
    {
        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        var (hi, lo) = ColumnValue.SplitGuidForMerge(guid);

        Assert.Equal(ColumnStorageClass.Integral, hi.StorageClass);
        Assert.Equal(ColumnStorageClass.Integral, lo.StorageClass);
        Assert.Equal(6L, hi.SerialType); // 64-bit int
        Assert.Equal(6L, lo.SerialType); // 64-bit int
        Assert.Equal(0x0102030405060708L, hi.AsInt64());
        Assert.Equal(0x090a0b0c0d0e0f10L, lo.AsInt64());
    }

    [Fact]
    public void SplitGuidForMerge_EmptyGuid_ReturnsBothZero()
    {
        var (hi, lo) = ColumnValue.SplitGuidForMerge(Guid.Empty);
        Assert.Equal(0L, hi.AsInt64());
        Assert.Equal(0L, lo.AsInt64());
    }
}
