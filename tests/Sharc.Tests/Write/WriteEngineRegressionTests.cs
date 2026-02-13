using Sharc.Core;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Sharc.Core.Records;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Regression tests covering dangerous Write Engine gaps:
/// - Record → Encode → Decode pipeline (never tested as a unit)
/// - RecordEncoder >64 columns (heap allocation path)
/// - Page size variations (everything hardcoded to 4096)
/// - WalWriter bigEndian flag (constructor parameter, never tested)
/// - PageManager + WalWriter integration (commit flow)
/// </summary>
public sealed class WriteEngineRegressionTests
{
    // ═══════════════════════════════════════════════════════════════════
    // 6. Record → Encode → Decode end-to-end pipeline
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Pipeline_EncodeRecord_DecodeRecord_AllTypesPreserved()
    {
        var original = new[]
        {
            ColumnValue.Null(),
            ColumnValue.FromInt64(0, 42),
            ColumnValue.FromInt64(0, -999),
            ColumnValue.FromInt64(0, 0),
            ColumnValue.FromInt64(0, 1),
            ColumnValue.FromInt64(0, long.MaxValue),
            ColumnValue.FromDouble(3.14159),
            ColumnValue.FromDouble(-1.5),
            ColumnValue.FromDouble(0.0),
            ColumnValue.Text(0, Encoding.UTF8.GetBytes("Hello, Sharc!")),
            ColumnValue.Text(0, Array.Empty<byte>()),
            ColumnValue.Blob(0, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            ColumnValue.Blob(0, Array.Empty<byte>()),
        };

        // Encode
        int size = RecordEncoder.ComputeEncodedSize(original);
        var buffer = new byte[size];
        int written = RecordEncoder.EncodeRecord(original, buffer);
        Assert.Equal(size, written);

        // Decode
        var decoder = new RecordDecoder();
        var decoded = decoder.DecodeRecord(buffer);

        Assert.Equal(original.Length, decoded.Length);

        // NULL
        Assert.True(decoded[0].IsNull);
        // Integers
        Assert.Equal(42L, decoded[1].AsInt64());
        Assert.Equal(-999L, decoded[2].AsInt64());
        Assert.Equal(0L, decoded[3].AsInt64());
        Assert.Equal(1L, decoded[4].AsInt64());
        Assert.Equal(long.MaxValue, decoded[5].AsInt64());
        // Doubles
        Assert.Equal(3.14159, decoded[6].AsDouble());
        Assert.Equal(-1.5, decoded[7].AsDouble());
        Assert.Equal(0.0, decoded[8].AsDouble());
        // Text
        Assert.Equal("Hello, Sharc!", Encoding.UTF8.GetString(decoded[9].AsBytes().Span));
        Assert.Empty(decoded[10].AsBytes().Span.ToArray());
        // Blob
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, decoded[11].AsBytes().Span.ToArray());
        Assert.Empty(decoded[12].AsBytes().Span.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════
    // 7. RecordEncoder >64 columns (heap allocation path)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void EncodeRecord_65Columns_HeapAllocation_RoundTrips()
    {
        // 65 columns = 1 past the stackalloc threshold (64)
        var columns = new ColumnValue[65];
        for (int i = 0; i < 65; i++)
            columns[i] = ColumnValue.FromInt64(0, i * 7);

        int size = RecordEncoder.ComputeEncodedSize(columns);
        var buffer = new byte[size];
        int written = RecordEncoder.EncodeRecord(columns, buffer);
        Assert.Equal(size, written);

        var decoder = new RecordDecoder();
        var decoded = decoder.DecodeRecord(buffer);

        Assert.Equal(65, decoded.Length);
        for (int i = 0; i < 65; i++)
            Assert.Equal(i * 7L, decoded[i].AsInt64());
    }

    [Fact]
    public void EncodeRecord_200Columns_MixedTypes_RoundTrips()
    {
        // 200 columns — well past heap threshold, with mixed types
        var columns = new ColumnValue[200];
        for (int i = 0; i < 200; i++)
        {
            columns[i] = (i % 5) switch
            {
                0 => ColumnValue.Null(),
                1 => ColumnValue.FromInt64(0, i * 13 - 500),
                2 => ColumnValue.FromDouble(i * 0.7),
                3 => ColumnValue.Text(0, Encoding.UTF8.GetBytes($"col_{i}")),
                _ => ColumnValue.Blob(0, new byte[] { (byte)(i & 0xFF), (byte)((i >> 8) & 0xFF) }),
            };
        }

        int size = RecordEncoder.ComputeEncodedSize(columns);
        var buffer = new byte[size];
        int written = RecordEncoder.EncodeRecord(columns, buffer);
        Assert.Equal(size, written);

        var decoder = new RecordDecoder();
        var decoded = decoder.DecodeRecord(buffer);

        Assert.Equal(200, decoded.Length);

        // Spot-check representative values from each type
        Assert.True(decoded[0].IsNull);
        Assert.Equal(1 * 13 - 500, decoded[1].AsInt64());
        Assert.Equal(2 * 0.7, decoded[2].AsDouble());
        Assert.Equal("col_3", Encoding.UTF8.GetString(decoded[3].AsBytes().Span));
        Assert.Equal(new byte[] { 4, 0 }, decoded[4].AsBytes().Span.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════
    // 8. Page size variations (everything was hardcoded to 4096)
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(8192)]
    [InlineData(16384)]
    [InlineData(65536)]
    public void WalWriter_PageSizeVariations_RoundTrips(int pageSize)
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, pageSize);
        writer.WriteHeader();

        byte[] pageData = new byte[pageSize];
        Array.Fill(pageData, (byte)0xAB);
        writer.AppendCommitFrame(1, pageData, 3);

        var walData = stream.ToArray();

        // Verify header parses with correct page size
        var header = WalHeader.Parse(walData);
        Assert.Equal(pageSize, header.PageSize);

        // Verify frame map is readable
        var frameMap = WalReader.ReadFrameMap(walData, pageSize);
        Assert.Single(frameMap);
        Assert.True(frameMap.ContainsKey(1));

        // Verify page data is preserved
        long dataOffset = frameMap[1];
        var readBack = walData.AsSpan((int)dataOffset, pageSize);
        Assert.Equal(pageData, readBack.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════
    // 9. WalWriter bigEndian flag (constructor parameter, never tested)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WalWriter_BigEndianChecksums_FrameMapReadable()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, 4096, bigEndianChecksums: true);
        writer.WriteHeader();

        byte[] pageData = new byte[4096];
        Array.Fill(pageData, (byte)0xCC);
        writer.AppendCommitFrame(1, pageData, 5);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, 4096);

        Assert.Single(frameMap);
        Assert.True(frameMap.ContainsKey(1));
    }

    [Fact]
    public void WalWriter_BigEndianChecksums_PageDataPreserved()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, 4096, bigEndianChecksums: true);
        writer.WriteHeader();

        byte[] pageData = new byte[4096];
        for (int i = 0; i < pageData.Length; i++)
            pageData[i] = (byte)(i & 0xFF);
        writer.AppendCommitFrame(1, pageData, 5);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, 4096);
        long dataOffset = frameMap[1];
        var readBack = walData.AsSpan((int)dataOffset, 4096);

        Assert.Equal(pageData, readBack.ToArray());
    }

    [Fact]
    public void WalWriter_BigEndianChecksums_MultipleFrames_AllValid()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, 4096, bigEndianChecksums: true);
        writer.WriteHeader();

        for (uint i = 1; i <= 3; i++)
        {
            byte[] page = new byte[4096];
            Array.Fill(page, (byte)i);
            writer.AppendFrame(i, page);
        }

        byte[] commitPage = new byte[4096];
        Array.Fill(commitPage, (byte)0xFF);
        writer.AppendCommitFrame(4, commitPage, 10);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, 4096);

        // All 4 frames should be in the committed map
        Assert.Equal(4, frameMap.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 10. PageManager + WalWriter integration (commit flow)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PageManager_WalWriter_CommitFlow_PagesReadable()
    {
        const int pageSize = 4096;

        // Create a mock page source with 2 existing pages
        var source = new InMemoryPageSource(pageSize, 2);

        using var pm = new PageManager(source);

        // Dirty page 1 (COW from source)
        var page1 = pm.GetPageForWrite(1);
        page1[0] = 0xAA;
        page1[1] = 0xBB;

        // Dirty page 2
        var page2 = pm.GetPageForWrite(2);
        page2[0] = 0xCC;

        // Allocate a new page
        uint newPageNum = pm.AllocatePage();
        var newPage = pm.GetPageForWrite(newPageNum);
        newPage[0] = 0xDD;

        // Write dirty pages to WAL
        using var walStream = new MemoryStream();
        using var walWriter = new WalWriter(walStream, pageSize);
        walWriter.WriteHeader();

        var dirtyPages = pm.GetDirtyPages().ToList();
        for (int i = 0; i < dirtyPages.Count - 1; i++)
            walWriter.AppendFrame((uint)dirtyPages[i].PageNumber, dirtyPages[i].Data.Span);

        var last = dirtyPages[^1];
        walWriter.AppendCommitFrame((uint)last.PageNumber, last.Data.Span, (uint)(2 + 1));

        // Verify WAL is readable
        var walData = walStream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, pageSize);

        Assert.Equal(dirtyPages.Count, frameMap.Count);

        // Verify page 1 data round-tripped through the entire flow
        Assert.True(frameMap.ContainsKey(1));
        long offset1 = frameMap[1];
        Assert.Equal(0xAA, walData[(int)offset1]);
        Assert.Equal(0xBB, walData[(int)offset1 + 1]);

        // Reset should release all buffers
        pm.Reset();
    }

    /// <summary>
    /// Minimal IPageSource for testing PageManager without real database files.
    /// </summary>
    private sealed class InMemoryPageSource : IPageSource
    {
        private readonly byte[][] _pages;
        public int PageSize { get; }
        public int PageCount { get; }

        public InMemoryPageSource(int pageSize, int pageCount)
        {
            PageSize = pageSize;
            PageCount = pageCount;
            _pages = new byte[pageCount][];
            for (int i = 0; i < pageCount; i++)
                _pages[i] = new byte[pageSize];
        }

        public int ReadPage(uint pageNumber, Span<byte> destination)
        {
            _pages[pageNumber - 1].AsSpan(0, PageSize).CopyTo(destination);
            return PageSize;
        }

        public ReadOnlySpan<byte> GetPage(uint pageNumber)
        {
            return _pages[pageNumber - 1].AsSpan(0, PageSize);
        }

        public void Dispose() { }
    }
}
