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

using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Inference-based tests for DatabaseHeader parsing.
/// Verifies edge cases derived from the SQLite file format specification.
/// </summary>
public class DatabaseHeaderInferenceTests
{
    private static byte[] MakeHeader(Action<byte[]>? customize = null)
    {
        var data = new byte[100];
        "SQLite format 3\0"u8.CopyTo(data);
        // Default: 4096 page size
        data[16] = 0x10; data[17] = 0x00;
        // Write/read version
        data[18] = 1; data[19] = 1;
        // Page count = 1
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 1);
        // Schema format = 4
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);
        // Text encoding = 1 (UTF-8)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(56), 1);
        customize?.Invoke(data);
        return data;
    }

    // --- Page size 1 encodes 65536 ---
    // SQLite spec: "If the page size is 1, that is interpreted as 65536."

    [Fact]
    public void Parse_PageSize1_Returns65536()
    {
        var data = MakeHeader(d =>
        {
            d[16] = 0x00; d[17] = 0x01; // raw value = 1
        });

        var header = DatabaseHeader.Parse(data);
        Assert.Equal(65536, header.PageSize);
    }

    [Fact]
    public void Parse_PageSize512_Returns512()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(16), 512);
        });

        var header = DatabaseHeader.Parse(data);
        Assert.Equal(512, header.PageSize);
    }

    // --- Reserved bytes reduce usable page size ---
    // UsablePageSize = PageSize - ReservedBytesPerPage

    [Fact]
    public void Parse_ReservedBytes0_UsableEqualsPageSize()
    {
        var data = MakeHeader(d => { d[20] = 0; });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(header.PageSize, header.UsablePageSize);
    }

    [Fact]
    public void Parse_ReservedBytes64_ReducesUsableSize()
    {
        // Encrypted databases commonly reserve 64 bytes per page for nonce + MAC
        var data = MakeHeader(d => { d[20] = 64; });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(4096 - 64, header.UsablePageSize);
        Assert.Equal(64, header.ReservedBytesPerPage);
    }

    // --- Text encoding field (offset 56) ---

    [Fact]
    public void Parse_TextEncodingUtf8_Returns1()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(56), 1);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(1, header.TextEncoding);
    }

    [Fact]
    public void Parse_TextEncodingUtf16Le_Returns2()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(56), 2);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(2, header.TextEncoding);
    }

    [Fact]
    public void Parse_TextEncodingUtf16Be_Returns3()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(56), 3);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(3, header.TextEncoding);
    }

    // --- WAL mode detection ---
    // WAL mode is indicated by write version = 2 or read version = 2

    [Fact]
    public void Parse_WriteVersion2_IsWalMode()
    {
        var data = MakeHeader(d => { d[18] = 2; d[19] = 2; });
        var header = DatabaseHeader.Parse(data);
        Assert.True(header.IsWalMode);
    }

    [Fact]
    public void Parse_WriteVersion1ReadVersion1_NotWalMode()
    {
        var data = MakeHeader(d => { d[18] = 1; d[19] = 1; });
        var header = DatabaseHeader.Parse(data);
        Assert.False(header.IsWalMode);
    }

    // --- Schema format (offset 44) ---

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Parse_SchemaFormat_PreservesValue(int format)
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(44), (uint)format);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(format, header.SchemaFormat);
    }

    // --- Application ID (offset 68) ---

    [Fact]
    public void Parse_ApplicationId_PreservesUInt32()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(68), 0x12345678);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(0x12345678, header.ApplicationId);
    }

    // --- User version (offset 60) ---

    [Fact]
    public void Parse_UserVersion_PreservesValue()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(60), 42);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(42, header.UserVersion);
    }

    // --- Change counter (offset 24) ---

    [Fact]
    public void Parse_ChangeCounter_PreservesValue()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(24), 999);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(999u, header.ChangeCounter);
    }

    // --- Freelist fields ---

    [Fact]
    public void Parse_FreelistPage_ReadsOffset32()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(32), 5);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(5u, header.FirstFreelistPage);
    }

    [Fact]
    public void Parse_FreelistPageCount_ReadsOffset36()
    {
        var data = MakeHeader(d =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(36), 3);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(3, header.FreelistPageCount);
    }

    // --- Exactly 100 bytes is minimum ---

    [Fact]
    public void Parse_Exactly100Bytes_Succeeds()
    {
        var data = MakeHeader();
        Assert.Equal(100, data.Length);

        var header = DatabaseHeader.Parse(data);
        Assert.Equal(4096, header.PageSize);
    }

    [Fact]
    public void Parse_99Bytes_ThrowsInvalidDatabase()
    {
        var data = MakeHeader()[..99];
        Assert.Throws<InvalidDatabaseException>(() => DatabaseHeader.Parse(data));
    }

    // --- HasValidMagic: the null terminator at byte 15 is part of the magic ---

    [Fact]
    public void HasValidMagic_NullTerminatorMissing_ReturnsFalse()
    {
        var data = MakeHeader();
        data[15] = (byte)'!'; // Replace null terminator
        Assert.False(DatabaseHeader.HasValidMagic(data));
    }

    [Fact]
    public void HasValidMagic_LessThan16Bytes_ReturnsFalse()
    {
        Assert.False(DatabaseHeader.HasValidMagic(new byte[15]));
    }

    // --- SQLite version number (offset 96) ---

    [Fact]
    public void Parse_SqliteVersionNumber_PreservesValue()
    {
        var data = MakeHeader(d =>
        {
            // SQLite 3.39.4 → 3039004
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(96), 3039004);
        });
        var header = DatabaseHeader.Parse(data);
        Assert.Equal(3039004, header.SqliteVersionNumber);
    }
}
