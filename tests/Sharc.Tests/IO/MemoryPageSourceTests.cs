using FluentAssertions;
using Sharc.Core.IO;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.IO;

public class MemoryPageSourceTests
{
    private static byte[] CreateMinimalDatabase(int pageSize = 4096, int pageCount = 3)
    {
        var data = new byte[pageSize * pageCount];
        WriteHeader(data, pageSize, pageCount);
        return data;
    }

    private static void WriteHeader(byte[] data, int pageSize, int pageCount)
    {
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1; // write/read version
        data[20] = 0;  // reserved
        data[21] = 64; // max embedded payload fraction
        data[22] = 32; // min embedded payload fraction
        data[23] = 32; // leaf payload fraction
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;  // schema format
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1; // UTF-8
    }

    [Fact]
    public void Constructor_ValidDatabase_ParsesHeaderCorrectly()
    {
        var data = CreateMinimalDatabase(pageSize: 4096, pageCount: 3);
        using var source = new MemoryPageSource(data);

        source.PageSize.Should().Be(4096);
        source.PageCount.Should().Be(3);
    }

    [Fact]
    public void Constructor_InvalidHeader_ThrowsInvalidDatabaseException()
    {
        var data = new byte[4096]; // all zeros — no valid magic

        Action act = () => _ = new MemoryPageSource(data);

        act.Should().Throw<InvalidDatabaseException>();
    }

    [Fact]
    public void GetPage_Page1_ReturnsFirstPageSpan()
    {
        var data = CreateMinimalDatabase();
        // Write a known byte at offset 100 (inside page 1, after header)
        data[100] = 0xAB;

        using var source = new MemoryPageSource(data);
        var page = source.GetPage(1);

        page.Length.Should().Be(4096);
        page[100].Should().Be(0xAB);
    }

    [Fact]
    public void GetPage_Page2_ReturnsSecondPageSpan()
    {
        var data = CreateMinimalDatabase(pageSize: 4096, pageCount: 3);
        // Write a known byte at start of page 2
        data[4096] = 0xCD;

        using var source = new MemoryPageSource(data);
        var page = source.GetPage(2);

        page.Length.Should().Be(4096);
        page[0].Should().Be(0xCD);
    }

    [Fact]
    public void GetPage_ZeroCopy_ReturnsSameUnderlyingData()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        var page1 = source.GetPage(1);
        var page1Again = source.GetPage(1);

        // Both spans should reflect the same underlying data
        page1[0].Should().Be(page1Again[0]);
    }

    [Fact]
    public void GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        Action act = () => { _ = source.GetPage(0); };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase(pageCount: 3);
        using var source = new MemoryPageSource(data);

        Action act = () => { _ = source.GetPage(4); };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadPage_CopiesDataToDestination()
    {
        var data = CreateMinimalDatabase();
        data[100] = 0xEF;

        using var source = new MemoryPageSource(data);
        var buffer = new byte[4096];
        var bytesRead = source.ReadPage(1, buffer);

        bytesRead.Should().Be(4096);
        buffer[100].Should().Be(0xEF);
    }

    [Fact]
    public void ReadPage_ReturnsPageSize()
    {
        var data = CreateMinimalDatabase(pageSize: 1024, pageCount: 5);
        using var source = new MemoryPageSource(data);
        var buffer = new byte[1024];

        source.ReadPage(1, buffer).Should().Be(1024);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var data = CreateMinimalDatabase();
        var source = new MemoryPageSource(data);

        source.Dispose();
        source.Dispose(); // should not throw
    }
}
