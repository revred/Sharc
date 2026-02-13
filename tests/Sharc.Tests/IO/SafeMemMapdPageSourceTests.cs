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

using Sharc.Core.IO;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.IO;

public class SafeMemMapdPageSourceTests : IDisposable
{
    private readonly string _tempDir;

    public SafeMemMapdPageSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharc_test_mmap_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private string CreateDatabaseFile(int pageSize = 4096, int pageCount = 3, byte? markerAtPage2 = null)
    {
        var data = new byte[pageSize * pageCount];
        WriteHeader(data, pageSize, pageCount);
        if (markerAtPage2.HasValue)
            data[pageSize] = markerAtPage2.Value; // first byte of page 2

        var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.db");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static void WriteHeader(byte[] data, int pageSize, int pageCount)
    {
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[20] = 0;
        data[21] = 64; data[22] = 32; data[23] = 32;
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1;
    }

    // --- Open / header parse ---

    [Fact]
    public void Constructor_ValidFile_ParsesHeader()
    {
        var path = CreateDatabaseFile(pageSize: 4096, pageCount: 5);

        using var source = new SafeMemMapdPageSource(path);

        Assert.Equal(4096, source.PageSize);
        Assert.Equal(5, source.PageCount);
    }

    [Fact]
    public void Constructor_SmallPageSize_ParsesCorrectly()
    {
        var path = CreateDatabaseFile(pageSize: 1024, pageCount: 10);

        using var source = new SafeMemMapdPageSource(path);

        Assert.Equal(1024, source.PageSize);
        Assert.Equal(10, source.PageCount);
    }

    [Fact]
    public void Constructor_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => _ = new SafeMemMapdPageSource(Path.Combine(_tempDir, "nonexistent.db")));
    }

    [Fact]
    public void Constructor_EmptyFile_ThrowsArgumentException()
    {
        var path = Path.Combine(_tempDir, "empty.db");
        File.WriteAllBytes(path, []);

        var ex = Assert.Throws<ArgumentException>(() => _ = new SafeMemMapdPageSource(path));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_InvalidMagic_ThrowsInvalidDatabaseException()
    {
        var path = Path.Combine(_tempDir, "bad_magic.db");
        File.WriteAllBytes(path, new byte[4096]); // all zeros

        Assert.Throws<InvalidDatabaseException>(() => _ = new SafeMemMapdPageSource(path));
    }

    // --- GetPage ---

    [Fact]
    public void GetPage_Page1_ReturnsCorrectData()
    {
        var path = CreateDatabaseFile();

        using var source = new SafeMemMapdPageSource(path);
        var page = source.GetPage(1);

        Assert.Equal(4096, page.Length);
        // First 16 bytes should be the SQLite magic string
        Assert.Equal((byte)'S', page[0]);
        Assert.Equal((byte)'Q', page[1]);
        Assert.Equal((byte)'L', page[2]);
    }

    [Fact]
    public void GetPage_Page2_ReturnsMarkerByte()
    {
        var path = CreateDatabaseFile(markerAtPage2: 0xBE);

        using var source = new SafeMemMapdPageSource(path);
        var page = source.GetPage(2);

        Assert.Equal(4096, page.Length);
        Assert.Equal(0xBE, page[0]);
    }

    [Fact]
    public void GetPage_LastPage_ReturnsData()
    {
        var path = CreateDatabaseFile(pageCount: 5);

        using var source = new SafeMemMapdPageSource(path);
        var page = source.GetPage(5);

        Assert.Equal(4096, page.Length);
    }

    [Fact]
    public void GetPage_ZeroCopy_ConsecutiveCallsReturnSameData()
    {
        var path = CreateDatabaseFile();

        using var source = new SafeMemMapdPageSource(path);
        var first = source.GetPage(1);
        var second = source.GetPage(1);

        Assert.True(first.SequenceEqual(second));
    }

    [Fact]
    public void GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var path = CreateDatabaseFile();
        using var source = new SafeMemMapdPageSource(path);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = source.GetPage(0); });
    }

    [Fact]
    public void GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var path = CreateDatabaseFile(pageCount: 3);
        using var source = new SafeMemMapdPageSource(path);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = source.GetPage(4); });
    }

    // --- ReadPage ---

    [Fact]
    public void ReadPage_CopiesDataIntoBuffer()
    {
        var path = CreateDatabaseFile(markerAtPage2: 0xFE);
        using var source = new SafeMemMapdPageSource(path);

        var buffer = new byte[4096];
        var bytesRead = source.ReadPage(2, buffer);

        Assert.Equal(4096, bytesRead);
        Assert.Equal(0xFE, buffer[0]);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_ReleasesFileHandle()
    {
        var path = CreateDatabaseFile();
        var source = new SafeMemMapdPageSource(path);
        source.Dispose();

        // File should be accessible after dispose
        Assert.True(File.Exists(path));
        File.Delete(path); // should not throw â€” handle released
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var path = CreateDatabaseFile();
        var source = new SafeMemMapdPageSource(path);

        source.Dispose();
        source.Dispose(); // should not throw
    }

    [Fact]
    public void GetPage_AfterDispose_ThrowsObjectDisposedException()
    {
        var path = CreateDatabaseFile();
        var source = new SafeMemMapdPageSource(path);
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = source.GetPage(1); });
    }

    // --- Multiple pages in sequence (simulates scan) ---

    [Fact]
    public void GetPage_AllPages_ReturnsDistinctSlices()
    {
        var path = CreateDatabaseFile(pageSize: 1024, pageCount: 8);
        using var source = new SafeMemMapdPageSource(path);

        for (uint p = 1; p <= 8; p++)
        {
            var page = source.GetPage(p);
            Assert.Equal(1024, page.Length);
        }
    }
}
