/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
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
        var val = ColumnValue.Integer(4, 42);
        Assert.False(val.IsNull);
        Assert.Equal(ColumnStorageClass.Integer, val.StorageClass);
        Assert.Equal(42L, val.AsInt64());
    }

    [Fact]
    public void Integer_NegativeValue_Works()
    {
        var val = ColumnValue.Integer(6, -9999);
        Assert.Equal(-9999L, val.AsInt64());
    }

    [Fact]
    public void Float_StoresAndRetrievesValue()
    {
        var val = ColumnValue.Float(3.14159);
        Assert.Equal(ColumnStorageClass.Float, val.StorageClass);
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
        var val = ColumnValue.Integer(8, 0);
        Assert.Equal(8L, val.SerialType);
        Assert.Equal(0L, val.AsInt64());
    }

    [Fact]
    public void Integer_ConstantOne_SerialType9()
    {
        var val = ColumnValue.Integer(9, 1);
        Assert.Equal(9L, val.SerialType);
        Assert.Equal(1L, val.AsInt64());
    }
}
