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
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for SQLite serial type interpretation.
/// </summary>
public class SerialTypeCodecTests
{
    // --- Content size ---

    [Theory]
    [InlineData(0, 0)]   // NULL
    [InlineData(1, 1)]   // 8-bit int
    [InlineData(2, 2)]   // 16-bit int
    [InlineData(3, 3)]   // 24-bit int
    [InlineData(4, 4)]   // 32-bit int
    [InlineData(5, 6)]   // 48-bit int
    [InlineData(6, 8)]   // 64-bit int
    [InlineData(7, 8)]   // IEEE 754 float
    [InlineData(8, 0)]   // Integer constant 0
    [InlineData(9, 0)]   // Integer constant 1
    public void GetContentSize_FixedTypes_ReturnsCorrectSize(long serialType, int expectedSize)
    {
        Assert.Equal(expectedSize, SerialTypeCodec.GetContentSize(serialType));
    }

    [Theory]
    [InlineData(12, 0)]   // BLOB length 0
    [InlineData(14, 1)]   // BLOB length 1
    [InlineData(16, 2)]   // BLOB length 2
    [InlineData(100, 44)] // BLOB length (100-12)/2 = 44
    public void GetContentSize_BlobTypes_ReturnsCalculatedSize(long serialType, int expectedSize)
    {
        Assert.Equal(expectedSize, SerialTypeCodec.GetContentSize(serialType));
    }

    [Theory]
    [InlineData(13, 0)]   // TEXT length 0
    [InlineData(15, 1)]   // TEXT length 1
    [InlineData(17, 2)]   // TEXT length 2
    [InlineData(101, 44)] // TEXT length (101-13)/2 = 44
    public void GetContentSize_TextTypes_ReturnsCalculatedSize(long serialType, int expectedSize)
    {
        Assert.Equal(expectedSize, SerialTypeCodec.GetContentSize(serialType));
    }

    // --- Storage class ---

    [Fact]
    public void GetStorageClass_Null_ReturnsNull()
    {
        Assert.Equal(ColumnStorageClass.Null, SerialTypeCodec.GetStorageClass(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    public void GetStorageClass_IntegerTypes_ReturnsInteger(long serialType)
    {
        Assert.Equal(ColumnStorageClass.Integral, SerialTypeCodec.GetStorageClass(serialType));
    }

    [Fact]
    public void GetStorageClass_Float_ReturnsFloat()
    {
        Assert.Equal(ColumnStorageClass.Real, SerialTypeCodec.GetStorageClass(7));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(100)]
    public void GetStorageClass_BlobTypes_ReturnsBlob(long serialType)
    {
        Assert.Equal(ColumnStorageClass.Blob, SerialTypeCodec.GetStorageClass(serialType));
    }

    [Theory]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(101)]
    public void GetStorageClass_TextTypes_ReturnsText(long serialType)
    {
        Assert.Equal(ColumnStorageClass.Text, SerialTypeCodec.GetStorageClass(serialType));
    }

    // --- Type predicates ---

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void GetStorageClass_ReservedTypes_ThrowsOrReturnsNull(long serialType)
    {
        // Decision: reserved types 10, 11 are not used â€” treat as error
        Assert.Throws<ArgumentOutOfRangeException>(() => SerialTypeCodec.GetContentSize(serialType));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(13, false)]
    public void IsNull_VariousTypes_ReturnsCorrectly(long serialType, bool expected)
    {
        Assert.Equal(expected, SerialTypeCodec.IsNull(serialType));
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public void IsText_VariousTypes_ReturnsCorrectly(long serialType, bool expected)
    {
        Assert.Equal(expected, SerialTypeCodec.IsText(serialType));
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    public void IsBlob_VariousTypes_ReturnsCorrectly(long serialType, bool expected)
    {
        Assert.Equal(expected, SerialTypeCodec.IsBlob(serialType));
    }

    // --- GetSerialType inverse mapping ---

    [Fact]
    public void GetSerialType_Null_Returns0()
    {
        var value = ColumnValue.Null();
        Assert.Equal(0L, SerialTypeCodec.GetSerialType(value));
    }

    [Theory]
    [InlineData(0, 8)]        // constant 0
    [InlineData(1, 9)]        // constant 1
    [InlineData(2, 1)]        // 8-bit
    [InlineData(127, 1)]      // 8-bit max
    [InlineData(-128, 1)]     // 8-bit min
    [InlineData(128, 2)]      // 16-bit
    [InlineData(32767, 2)]    // 16-bit max
    [InlineData(-32768, 2)]   // 16-bit min
    [InlineData(32768, 3)]    // 24-bit
    [InlineData(8388607, 3)]  // 24-bit max
    [InlineData(8388608, 4)]  // 32-bit
    [InlineData(2147483647, 4)]       // 32-bit max
    [InlineData(2147483648L, 5)]      // 48-bit
    [InlineData(140737488355328L, 6)] // 64-bit (over 48-bit max)
    public void GetSerialType_IntegerBoundaries_ReturnsCorrectTypes(long intValue, long expectedType)
    {
        var value = ColumnValue.FromInt64(4, intValue);
        Assert.Equal(expectedType, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_Real_Returns7()
    {
        var value = ColumnValue.FromDouble(3.14);
        Assert.Equal(7L, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_Text_ReturnsOddCodeGte13()
    {
        var value = ColumnValue.Text(13 + 2 * 5, "hello"u8.ToArray());
        long st = SerialTypeCodec.GetSerialType(value);
        Assert.Equal(23L, st); // 2*5+13 = 23
        Assert.True(st >= 13 && (st & 1) == 1);
    }

    [Fact]
    public void GetSerialType_Blob_ReturnsEvenCodeGte12()
    {
        var value = ColumnValue.Blob(12 + 2 * 3, new byte[3]);
        long st = SerialTypeCodec.GetSerialType(value);
        Assert.Equal(18L, st); // 2*3+12 = 18
        Assert.True(st >= 12 && (st & 1) == 0);
    }

    // --- IsIntegral / IsReal predicates ---

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(0, false)]   // NULL
    [InlineData(7, false)]   // Real
    [InlineData(13, false)]  // Text
    public void IsIntegral_VariousTypes_ReturnsCorrectly(long serialType, bool expected)
    {
        Assert.Equal(expected, SerialTypeCodec.IsIntegral(serialType));
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(13, false)]
    public void IsReal_VariousTypes_ReturnsCorrectly(long serialType, bool expected)
    {
        Assert.Equal(expected, SerialTypeCodec.IsReal(serialType));
    }
}
