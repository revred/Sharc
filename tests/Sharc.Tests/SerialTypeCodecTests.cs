/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
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
}
