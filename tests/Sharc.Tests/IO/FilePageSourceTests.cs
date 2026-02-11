using FluentAssertions;
using Sharc.Core.IO;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.IO;

public class FilePageSourceTests : IDisposable
{
    private readonly string _tempDir;

    public FilePageSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharc_test_file_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
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
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1; // UTF-8
    }

    // --- Open / header parse ---

    [Fact]
    public void Constructor_ValidFile_ParsesHeader()
    {
        var path = CreateDatabaseFile(pageSize: 4096, pageCount: 5);

        using var source = new FilePageSource(path);

        source.PageSize.Should().Be(4096);
        source.PageCount.Should().Be(5);
    }

    [Fact]
    public void Constructor_SmallPageSize_ParsesCorrectly()
    {
        var path = CreateDatabaseFile(pageSize: 1024, pageCount: 10);

        using var source = new FilePageSource(path);

        source.PageSize.Should().Be(1024);
        source.PageCount.Should().Be(10);
    }

    [Fact]
    public void Constructor_FileNotFound_ThrowsFileNotFoundException()
    {
        Action act = () => _ = new FilePageSource(Path.Combine(_tempDir, "nonexistent.db"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Constructor_EmptyFile_ThrowsArgumentException()
    {
        var path = Path.Combine(_tempDir, "empty.db");
        File.WriteAllBytes(path, []);

        Action act = () => _ = new FilePageSource(path);

        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void Constructor_InvalidMagic_ThrowsInvalidDatabaseException()
    {
        var path = Path.Combine(_tempDir, "bad_magic.db");
        File.WriteAllBytes(path, new byte[4096]); // all zeros

        Action act = () => _ = new FilePageSource(path);

        act.Should().Throw<InvalidDatabaseException>();
    }

    // --- GetPage ---

    [Fact]
    public void GetPage_Page1_ReturnsCorrectData()
    {
        var path = CreateDatabaseFile();

        using var source = new FilePageSource(path);
        var page = source.GetPage(1);

        page.Length.Should().Be(4096);
        page[0].Should().Be((byte)'S');
        page[1].Should().Be((byte)'Q');
        page[2].Should().Be((byte)'L');
    }

    [Fact]
    public void GetPage_Page2_ReturnsMarkerByte()
    {
        var path = CreateDatabaseFile(markerAtPage2: 0xBE);

        using var source = new FilePageSource(path);
        var page = source.GetPage(2);

        page.Length.Should().Be(4096);
        page[0].Should().Be(0xBE);
    }

    [Fact]
    public void GetPage_LastPage_ReturnsData()
    {
        var path = CreateDatabaseFile(pageCount: 5);

        using var source = new FilePageSource(path);
        var page = source.GetPage(5);

        page.Length.Should().Be(4096);
    }

    [Fact]
    public void GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var path = CreateDatabaseFile();
        using var source = new FilePageSource(path);

        Action act = () => { _ = source.GetPage(0); };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var path = CreateDatabaseFile(pageCount: 3);
        using var source = new FilePageSource(path);

        Action act = () => { _ = source.GetPage(4); };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- ReadPage ---

    [Fact]
    public void ReadPage_CopiesDataIntoBuffer()
    {
        var path = CreateDatabaseFile(markerAtPage2: 0xFE);
        using var source = new FilePageSource(path);

        var buffer = new byte[4096];
        var bytesRead = source.ReadPage(2, buffer);

        bytesRead.Should().Be(4096);
        buffer[0].Should().Be(0xFE);
    }

    [Fact]
    public void ReadPage_ReturnsPageSize()
    {
        var path = CreateDatabaseFile(pageSize: 1024, pageCount: 5);
        using var source = new FilePageSource(path);
        var buffer = new byte[1024];

        source.ReadPage(1, buffer).Should().Be(1024);
    }

    [Fact]
    public void ReadPage_BypassesInternalBuffer()
    {
        var path = CreateDatabaseFile(markerAtPage2: 0xAA);
        using var source = new FilePageSource(path);

        var dest = new byte[4096];
        source.ReadPage(2, dest);

        dest[0].Should().Be(0xAA);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_ReleasesFileHandle()
    {
        var path = CreateDatabaseFile();
        var source = new FilePageSource(path);
        source.Dispose();

        File.Exists(path).Should().BeTrue();
        File.Delete(path); // should not throw — handle released
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var path = CreateDatabaseFile();
        var source = new FilePageSource(path);

        source.Dispose();
        source.Dispose(); // should not throw
    }

    [Fact]
    public void GetPage_AfterDispose_ThrowsObjectDisposedException()
    {
        var path = CreateDatabaseFile();
        var source = new FilePageSource(path);
        source.Dispose();

        Action act = () => { _ = source.GetPage(1); };

        act.Should().Throw<ObjectDisposedException>();
    }

    // --- Sequential page access ---

    [Fact]
    public void GetPage_AllPages_ReturnsCorrectSize()
    {
        var path = CreateDatabaseFile(pageSize: 1024, pageCount: 8);
        using var source = new FilePageSource(path);

        for (uint p = 1; p <= 8; p++)
        {
            var page = source.GetPage(p);
            page.Length.Should().Be(1024);
        }
    }

    [Fact]
    public void GetPage_ReusesInternalBuffer_SubsequentCallOverwritesPrevious()
    {
        // Write distinct markers at page 2 and page 3
        var data = new byte[4096 * 4];
        WriteHeader(data, 4096, 4);
        data[4096] = 0xAA; // page 2 marker
        data[4096 * 2] = 0xBB; // page 3 marker

        var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.db");
        File.WriteAllBytes(path, data);

        using var source = new FilePageSource(path);

        // Read page 2 — internal buffer has 0xAA at [0]
        var page2 = source.GetPage(2);
        page2[0].Should().Be(0xAA);

        // Read page 3 — internal buffer is overwritten with 0xBB
        var page3 = source.GetPage(3);
        page3[0].Should().Be(0xBB);
    }
}
