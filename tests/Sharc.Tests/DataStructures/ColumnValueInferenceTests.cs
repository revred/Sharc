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

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Inference-based tests for the ColumnValue readonly struct.
/// Verifies behaviors derived from SQLite coercion rules and storage class contracts.
/// </summary>
public class ColumnValueInferenceTests
{
    // --- NULL coercion: SQLite NULL coerces to 0/0.0/"" in typed contexts ---

    [Fact]
    public void Null_AsInt64_ReturnsZero()
    {
        var val = ColumnValue.Null();
        Assert.Equal(0L, val.AsInt64());
    }

    [Fact]
    public void Null_AsDouble_ReturnsZero()
    {
        var val = ColumnValue.Null();
        Assert.Equal(0.0, val.AsDouble());
    }

    [Fact]
    public void Null_AsBytes_ReturnsEmptyMemory()
    {
        var val = ColumnValue.Null();
        Assert.True(val.AsBytes().IsEmpty);
    }

    [Fact]
    public void Null_StorageClass_IsNull()
    {
        var val = ColumnValue.Null();
        Assert.Equal(ColumnStorageClass.Null, val.StorageClass);
        Assert.True(val.IsNull);
    }

    [Fact]
    public void Null_SerialType_IsZero()
    {
        // Serial type 0 is NULL per SQLite spec
        var val = ColumnValue.Null();
        Assert.Equal(0L, val.SerialType);
    }

    // --- Integer boundary values ---

    [Fact]
    public void Integer_MaxValue_PreservesExactly()
    {
        var val = ColumnValue.Integer(6, long.MaxValue);
        Assert.Equal(long.MaxValue, val.AsInt64());
    }

    [Fact]
    public void Integer_MinValue_PreservesExactly()
    {
        var val = ColumnValue.Integer(6, long.MinValue);
        Assert.Equal(long.MinValue, val.AsInt64());
    }

    [Fact]
    public void Integer_Zero_PreservesExactly()
    {
        var val = ColumnValue.Integer(1, 0);
        Assert.Equal(0L, val.AsInt64());
    }

    [Fact]
    public void Integer_NegativeOne_PreservesExactly()
    {
        var val = ColumnValue.Integer(1, -1);
        Assert.Equal(-1L, val.AsInt64());
    }

    // --- Serial types 8/9: Constants encoded with zero body bytes ---
    // SQLite spec: serial type 8 → integer 0, serial type 9 → integer 1
    // These types have ContentSize=0 — the entire value is encoded in the type code.

    [Fact]
    public void Integer_SerialType8_Constant0_HasStorageClassInteger()
    {
        var val = ColumnValue.Integer(8, 0);
        Assert.Equal(ColumnStorageClass.Integer, val.StorageClass);
        Assert.Equal(0L, val.AsInt64());
        Assert.Equal(8L, val.SerialType);
        Assert.False(val.IsNull);
    }

    [Fact]
    public void Integer_SerialType9_Constant1_HasStorageClassInteger()
    {
        var val = ColumnValue.Integer(9, 1);
        Assert.Equal(ColumnStorageClass.Integer, val.StorageClass);
        Assert.Equal(1L, val.AsInt64());
        Assert.Equal(9L, val.SerialType);
    }

    // --- Float IEEE 754 special values ---

    [Fact]
    public void Float_PositiveInfinity_RoundTrips()
    {
        var val = ColumnValue.Float(double.PositiveInfinity);
        Assert.True(double.IsPositiveInfinity(val.AsDouble()));
    }

    [Fact]
    public void Float_NegativeInfinity_RoundTrips()
    {
        var val = ColumnValue.Float(double.NegativeInfinity);
        Assert.True(double.IsNegativeInfinity(val.AsDouble()));
    }

    [Fact]
    public void Float_NaN_RoundTrips()
    {
        var val = ColumnValue.Float(double.NaN);
        Assert.True(double.IsNaN(val.AsDouble()));
    }

    [Fact]
    public void Float_NegativeZero_Preserved()
    {
        var val = ColumnValue.Float(-0.0);
        // -0.0 equals 0.0 in IEEE 754, but bit pattern differs
        Assert.Equal(0.0, val.AsDouble());
        Assert.True(double.IsNegative(val.AsDouble()));
    }

    [Fact]
    public void Float_StorageClass_IsFloat()
    {
        var val = ColumnValue.Float(1.0);
        Assert.Equal(ColumnStorageClass.Float, val.StorageClass);
        Assert.Equal(7L, val.SerialType);
    }

    // --- Text: empty string is NOT null ---
    // SQLite distinguishes between NULL and empty string ''

    [Fact]
    public void Text_EmptyString_IsNotNull()
    {
        var val = ColumnValue.Text(13, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(ColumnStorageClass.Text, val.StorageClass);
        Assert.False(val.IsNull);
        Assert.Equal("", val.AsString());
    }

    [Fact]
    public void Text_Utf8Bytes_DecodesCorrectly()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Hello");
        var val = ColumnValue.Text(23, bytes); // serial type 23 → (23-13)/2 = 5 bytes
        Assert.Equal("Hello", val.AsString());
    }

    [Fact]
    public void Text_SerialType_Preserved()
    {
        var val = ColumnValue.Text(23, new byte[5]);
        Assert.Equal(23L, val.SerialType);
    }

    // --- Blob: empty blob is NOT null ---

    [Fact]
    public void Blob_EmptyBlob_IsNotNull()
    {
        var val = ColumnValue.Blob(12, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(ColumnStorageClass.Blob, val.StorageClass);
        Assert.False(val.IsNull);
        Assert.True(val.AsBytes().IsEmpty);
    }

    [Fact]
    public void Blob_DataPreserved()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        var val = ColumnValue.Blob(20, data); // serial type 20 → (20-12)/2 = 4 bytes
        Assert.Equal(data, val.AsBytes().ToArray());
    }

    // --- Storage class consistency across all factories ---

    [Theory]
    [InlineData(0, ColumnStorageClass.Null)]
    [InlineData(1, ColumnStorageClass.Integer)]
    [InlineData(2, ColumnStorageClass.Integer)]
    [InlineData(3, ColumnStorageClass.Integer)]
    [InlineData(4, ColumnStorageClass.Integer)]
    [InlineData(5, ColumnStorageClass.Integer)]
    [InlineData(6, ColumnStorageClass.Integer)]
    [InlineData(8, ColumnStorageClass.Integer)]
    [InlineData(9, ColumnStorageClass.Integer)]
    public void Integer_AllSerialTypes_HaveCorrectStorageClass(long serialType, ColumnStorageClass expected)
    {
        ColumnValue val;
        if (serialType == 0)
            val = ColumnValue.Null();
        else
            val = ColumnValue.Integer(serialType, 42);

        Assert.Equal(expected, val.StorageClass);
    }
}
