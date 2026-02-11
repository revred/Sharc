using FluentAssertions;
using Sharc.Core.Format;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for SQLite database header parsing.
/// </summary>
public class DatabaseHeaderTests
{
    private static byte[] CreateValidHeader(int pageSize = 4096, int pageCount = 100,
        int textEncoding = 1, byte writeVersion = 1, byte readVersion = 1)
    {
        var header = new byte[100];
        // Magic string
        "SQLite format 3\0"u8.CopyTo(header);
        // Page size (big-endian at offset 16)
        header[16] = (byte)(pageSize >> 8);
        header[17] = (byte)(pageSize & 0xFF);
        // Write/read versions
        header[18] = writeVersion;
        header[19] = readVersion;
        // Reserved bytes per page
        header[20] = 0;
        // Max embedded payload fraction (must be 64)
        header[21] = 64;
        // Min embedded payload fraction (must be 32)
        header[22] = 32;
        // Leaf payload fraction (must be 32)
        header[23] = 32;
        // Page count (big-endian at offset 28)
        header[28] = (byte)(pageCount >> 24);
        header[29] = (byte)(pageCount >> 16);
        header[30] = (byte)(pageCount >> 8);
        header[31] = (byte)(pageCount & 0xFF);
        // Schema format (big-endian at offset 44), default to 4
        header[47] = 4;
        // Text encoding (big-endian at offset 56)
        header[56] = (byte)(textEncoding >> 24);
        header[57] = (byte)(textEncoding >> 16);
        header[58] = (byte)(textEncoding >> 8);
        header[59] = (byte)(textEncoding & 0xFF);
        return header;
    }

    [Fact]
    public void Parse_ValidHeader_ReturnsCorrectPageSize()
    {
        var data = CreateValidHeader(pageSize: 4096);
        var header = DatabaseHeader.Parse(data);
        header.PageSize.Should().Be(4096);
    }

    [Fact]
    public void Parse_PageSizeOne_Returns65536()
    {
        // Page size value 1 means 65536
        var data = CreateValidHeader();
        data[16] = 0x00;
        data[17] = 0x01;
        var header = DatabaseHeader.Parse(data);
        header.PageSize.Should().Be(65536);
    }

    [Fact]
    public void Parse_ValidHeader_ReturnsCorrectPageCount()
    {
        var data = CreateValidHeader(pageCount: 42);
        var header = DatabaseHeader.Parse(data);
        header.PageCount.Should().Be(42);
    }

    [Fact]
    public void Parse_Utf8Encoding_ReturnsUtf8()
    {
        var data = CreateValidHeader(textEncoding: 1);
        var header = DatabaseHeader.Parse(data);
        header.TextEncoding.Should().Be(1);
    }

    [Fact]
    public void Parse_WalMode_DetectedCorrectly()
    {
        var data = CreateValidHeader(writeVersion: 2, readVersion: 2);
        var header = DatabaseHeader.Parse(data);
        header.IsWalMode.Should().BeTrue();
    }

    [Fact]
    public void Parse_LegacyMode_NotWal()
    {
        var data = CreateValidHeader(writeVersion: 1, readVersion: 1);
        var header = DatabaseHeader.Parse(data);
        header.IsWalMode.Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidMagic_ThrowsInvalidDatabaseException()
    {
        var data = new byte[100];
        "Not a SQLite db!\0"u8.CopyTo(data);

        var act = () => DatabaseHeader.Parse(data);
        act.Should().Throw<InvalidDatabaseException>();
    }

    [Fact]
    public void Parse_TooShort_ThrowsInvalidDatabaseException()
    {
        var data = new byte[50]; // Less than 100 bytes
        var act = () => DatabaseHeader.Parse(data);
        act.Should().Throw<InvalidDatabaseException>();
    }

    [Fact]
    public void HasValidMagic_ValidHeader_ReturnsTrue()
    {
        var data = CreateValidHeader();
        DatabaseHeader.HasValidMagic(data).Should().BeTrue();
    }

    [Fact]
    public void HasValidMagic_InvalidHeader_ReturnsFalse()
    {
        var data = new byte[100];
        DatabaseHeader.HasValidMagic(data).Should().BeFalse();
    }

    [Fact]
    public void Parse_UsablePageSize_AccountsForReservedBytes()
    {
        var data = CreateValidHeader(pageSize: 4096);
        data[20] = 8; // 8 reserved bytes per page
        var header = DatabaseHeader.Parse(data);
        header.UsablePageSize.Should().Be(4088);
    }
}
