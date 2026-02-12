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

        Assert.Equal(4096, source.PageSize);
        Assert.Equal(3, source.PageCount);
    }

    [Fact]
    public void Constructor_InvalidHeader_ThrowsInvalidDatabaseException()
    {
        var data = new byte[4096]; // all zeros â€” no valid magic

        Assert.Throws<InvalidDatabaseException>(() => _ = new MemoryPageSource(data));
    }

    [Fact]
    public void GetPage_Page1_ReturnsFirstPageSpan()
    {
        var data = CreateMinimalDatabase();
        // Write a known byte at offset 100 (inside page 1, after header)
        data[100] = 0xAB;

        using var source = new MemoryPageSource(data);
        var page = source.GetPage(1);

        Assert.Equal(4096, page.Length);
        Assert.Equal(0xAB, page[100]);
    }

    [Fact]
    public void GetPage_Page2_ReturnsSecondPageSpan()
    {
        var data = CreateMinimalDatabase(pageSize: 4096, pageCount: 3);
        // Write a known byte at start of page 2
        data[4096] = 0xCD;

        using var source = new MemoryPageSource(data);
        var page = source.GetPage(2);

        Assert.Equal(4096, page.Length);
        Assert.Equal(0xCD, page[0]);
    }

    [Fact]
    public void GetPage_ZeroCopy_ReturnsSameUnderlyingData()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        var page1 = source.GetPage(1);
        var page1Again = source.GetPage(1);

        // Both spans should reflect the same underlying data
        Assert.Equal(page1[0], page1Again[0]);
    }

    [Fact]
    public void GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = source.GetPage(0); });
    }

    [Fact]
    public void GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase(pageCount: 3);
        using var source = new MemoryPageSource(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = source.GetPage(4); });
    }

    [Fact]
    public void ReadPage_CopiesDataToDestination()
    {
        var data = CreateMinimalDatabase();
        data[100] = 0xEF;

        using var source = new MemoryPageSource(data);
        var buffer = new byte[4096];
        var bytesRead = source.ReadPage(1, buffer);

        Assert.Equal(4096, bytesRead);
        Assert.Equal(0xEF, buffer[100]);
    }

    [Fact]
    public void ReadPage_ReturnsPageSize()
    {
        var data = CreateMinimalDatabase(pageSize: 1024, pageCount: 5);
        using var source = new MemoryPageSource(data);
        var buffer = new byte[1024];

        Assert.Equal(1024, source.ReadPage(1, buffer));
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
