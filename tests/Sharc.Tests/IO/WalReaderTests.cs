using System.Buffers.Binary;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

public class WalReaderTests
{
    private const int PageSize = 4096;

    [Fact]
    public void ReadFrameMap_EmptyWal_ReturnsEmptyDictionary()
    {
        // WAL with valid header but no frames
        var walData = BuildWalFile();

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Empty(map);
    }

    [Fact]
    public void ReadFrameMap_SingleCommittedFrame_ReturnsSingleEntry()
    {
        var pageData = new byte[PageSize];
        pageData[0] = 0x42; // arbitrary content

        var walData = BuildWalFile(
            (pageNumber: 3, dbSize: 10, data: pageData));

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Single(map);
        Assert.True(map.ContainsKey(3));
    }

    [Fact]
    public void ReadFrameMap_UncommittedFrame_ReturnsEmptyDictionary()
    {
        var pageData = new byte[PageSize];

        // dbSize = 0 means non-commit frame
        var walData = BuildWalFile(
            (pageNumber: 3, dbSize: 0, data: pageData));

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Empty(map);
    }

    [Fact]
    public void ReadFrameMap_MultipleFramesSingleTransaction_ReturnsAll()
    {
        var page1 = new byte[PageSize];
        page1[0] = 0x01;
        var page2 = new byte[PageSize];
        page2[0] = 0x02;
        var page3 = new byte[PageSize];
        page3[0] = 0x03;

        // First two frames non-commit, last frame is commit
        var walData = BuildWalFile(
            (pageNumber: 2, dbSize: 0, data: page1),
            (pageNumber: 5, dbSize: 0, data: page2),
            (pageNumber: 7, dbSize: 10, data: page3));

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Equal(3, map.Count);
        Assert.True(map.ContainsKey(2));
        Assert.True(map.ContainsKey(5));
        Assert.True(map.ContainsKey(7));
    }

    [Fact]
    public void ReadFrameMap_SamePageOverwritten_ReturnsLatestOffset()
    {
        var page1 = new byte[PageSize];
        page1[0] = 0xAA;
        var page2 = new byte[PageSize];
        page2[0] = 0xBB;

        // Page 3 written twice — second write should win
        var walData = BuildWalFile(
            (pageNumber: 3, dbSize: 0, data: page1),
            (pageNumber: 3, dbSize: 10, data: page2));

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Single(map);
        Assert.True(map.ContainsKey(3));

        // The offset should point to the second frame's data
        long offset = map[3];
        // Second frame header starts at 32 (WAL header) + 24 (first frame header) + PageSize (first frame data)
        long expectedFrameDataOffset = 32 + (24 + PageSize) + 24;
        Assert.Equal(expectedFrameDataOffset, offset);
    }

    [Fact]
    public void ReadFrameMap_TwoTransactions_ReturnsBothCommittedSets()
    {
        var page1 = new byte[PageSize];
        var page2 = new byte[PageSize];
        var page3 = new byte[PageSize];

        // Transaction 1: page 2 commit
        // Transaction 2: page 5, page 7 commit
        var walData = BuildWalFile(
            (pageNumber: 2, dbSize: 5, data: page1),    // Tx1 commit
            (pageNumber: 5, dbSize: 0, data: page2),    // Tx2 non-commit
            (pageNumber: 7, dbSize: 10, data: page3));   // Tx2 commit

        var map = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Equal(3, map.Count);
    }

    [Fact]
    public void ReadFrameMap_InvalidSalt_StopsAtInvalidFrame()
    {
        // Build a WAL with one committed frame, then manually corrupt the second frame's salt
        var page1 = new byte[PageSize];
        var page2 = new byte[PageSize];

        var walData = BuildWalFile(
            (pageNumber: 2, dbSize: 5, data: page1));

        // Extend with a corrupted frame (wrong salt)
        var extended = new byte[walData.Length + 24 + PageSize];
        walData.CopyTo(extended, 0);

        // Write a frame with invalid salt at the extension position
        int frameOffset = walData.Length;
        BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(frameOffset), 3); // page number
        BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(frameOffset + 4), 10); // dbSize
        BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(frameOffset + 8), 0xBADBAD); // wrong salt1
        BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(frameOffset + 12), 0xBADBAD); // wrong salt2

        var map = WalReader.ReadFrameMap(extended, PageSize);

        // Should only have page 2 from the first valid frame
        Assert.Single(map);
        Assert.True(map.ContainsKey(2));
    }

    [Fact]
    public void ReadFrameMap_FrameOffsetPointsToPageData()
    {
        var pageData = new byte[PageSize];
        pageData[0] = 0xDE;
        pageData[1] = 0xAD;

        var walData = BuildWalFile(
            (pageNumber: 1, dbSize: 5, data: pageData));

        var map = WalReader.ReadFrameMap(walData, PageSize);

        // Offset should point to page data, which is after WAL header + frame header
        long offset = map[1];
        Assert.Equal(0xDE, walData[offset]);
        Assert.Equal(0xAD, walData[offset + 1]);
    }

    #region WAL Builder Helpers

    /// <summary>
    /// Builds a valid WAL file in memory with proper cumulative checksums.
    /// Uses magic 0x377F0682 (native byte order checksums — LE on x86).
    /// </summary>
    private static byte[] BuildWalFile(
        params (uint pageNumber, uint dbSize, byte[] data)[] frames)
    {
        const uint magic = 0x377F0682; // native byte order (LE on x86)
        const uint formatVersion = 3007000;
        uint salt1 = 0x01020304;
        uint salt2 = 0x05060708;

        int totalSize = 32 + frames.Length * (24 + PageSize);
        var wal = new byte[totalSize];

        // Write WAL header (header fields are always big-endian)
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(0), magic);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(4), formatVersion);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(8), (uint)PageSize);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(12), 0); // checkpoint seq
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(16), salt1);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(20), salt2);

        // Compute WAL header checksum (bytes 0-23, native/LE mode)
        WalChecksumNative(wal.AsSpan(0, 24), 0, 0, out uint s0, out uint s1);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(24), s0);
        BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(28), s1);

        int offset = 32;
        for (int i = 0; i < frames.Length; i++)
        {
            var (pageNumber, dbSize, data) = frames[i];

            // Write frame header (without checksums yet)
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset), pageNumber);
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset + 4), dbSize);
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset + 8), salt1);
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset + 12), salt2);

            // Copy page data
            data.AsSpan(0, PageSize).CopyTo(wal.AsSpan(offset + 24));

            // Compute cumulative checksum: first 8 bytes of frame header, then page data
            WalChecksumNative(wal.AsSpan(offset, 8), s0, s1, out s0, out s1);
            WalChecksumNative(wal.AsSpan(offset + 24, PageSize), s0, s1, out s0, out s1);

            // Write checksums into frame header
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset + 16), s0);
            BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset + 20), s1);

            offset += 24 + PageSize;
        }

        return wal;
    }

    /// <summary>
    /// Computes the SQLite WAL checksum over a span of data (must be 8-byte aligned).
    /// Native mode on x86: reads 32-bit words using little-endian byte order.
    /// </summary>
    private static void WalChecksumNative(ReadOnlySpan<byte> data, uint s0In, uint s1In,
        out uint s0Out, out uint s1Out)
    {
        uint s0 = s0In, s1 = s1In;
        for (int i = 0; i + 7 < data.Length; i += 8)
        {
            s0 += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) + s1;
            s1 += BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]) + s0;
        }
        s0Out = s0;
        s1Out = s1;
    }

    #endregion
}
