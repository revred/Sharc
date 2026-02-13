/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for WalWriter — writes WAL frames that WalReader can read back.
/// </summary>
public class WalWriterTests
{
    private const int PageSize = 4096;

    // ── WriteHeader ──

    [Fact]
    public void WriteHeader_Produces32Bytes_ParseableByWalHeader()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        var data = stream.ToArray();
        Assert.True(data.Length >= WalHeader.HeaderSize);

        var header = WalHeader.Parse(data);
        Assert.Equal(PageSize, header.PageSize);
        Assert.Equal(3007000u, header.FormatVersion);
    }

    [Fact]
    public void WriteHeader_ChecksumsValidForFirstFrame()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        var data = stream.ToArray();
        var header = WalHeader.Parse(data);

        // Verify the header checksums by re-computing
        uint s0 = 0, s1 = 0;
        bool bigEndian = !header.IsNativeByteOrder;
        WalReader.ComputeChecksum(data.AsSpan(0, 24), bigEndian, ref s0, ref s1);
        Assert.Equal(s0, header.Checksum1);
        Assert.Equal(s1, header.Checksum2);
    }

    // ── AppendFrame ──

    [Fact]
    public void AppendFrame_SingleFrame_ReadableByWalReader()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        byte[] pageData = MakePageData(0xAA);
        writer.AppendCommitFrame(1, pageData, 5);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Single(frameMap);
        Assert.True(frameMap.ContainsKey(1));
    }

    [Fact]
    public void AppendFrame_ThreeFrames_AllAppearInFrameMap()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        writer.AppendFrame(1, MakePageData(0x11));
        writer.AppendFrame(2, MakePageData(0x22));
        writer.AppendCommitFrame(3, MakePageData(0x33), 10);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Equal(3, frameMap.Count);
        Assert.True(frameMap.ContainsKey(1));
        Assert.True(frameMap.ContainsKey(2));
        Assert.True(frameMap.ContainsKey(3));
    }

    // ── AppendCommitFrame ──

    [Fact]
    public void AppendCommitFrame_DbSizeNonZero_IsCommitFrame()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        writer.AppendCommitFrame(1, MakePageData(0xFF), 42);

        var walData = stream.ToArray();
        // Read back the frame header
        int frameOffset = WalHeader.HeaderSize;
        var frameHeader = WalFrameHeader.Parse(walData.AsSpan(frameOffset));
        Assert.True(frameHeader.IsCommitFrame);
        Assert.Equal(42u, frameHeader.DbSizeAfterCommit);
    }

    [Fact]
    public void AppendFrame_NonCommit_NotInMapAlone()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        writer.AppendFrame(1, MakePageData(0xAA)); // non-commit

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        // Non-commit frame without a subsequent commit should not appear
        Assert.Empty(frameMap);
    }

    // ── Cumulative checksums ──

    [Fact]
    public void AppendFrame_FiveFrames_AllChecksumsValid()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        for (uint i = 1; i <= 4; i++)
            writer.AppendFrame(i, MakePageData((byte)i));
        writer.AppendCommitFrame(5, MakePageData(0x05), 20);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        // All 5 pages should be in the committed map
        Assert.Equal(5, frameMap.Count);
    }

    // ── Round-trip: page data preserved ──

    [Fact]
    public void RoundTrip_WrittenPageData_PreservedByWalReader()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        byte[] originalPage = MakePageData(0xCD);
        writer.AppendCommitFrame(7, originalPage, 10);

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        Assert.True(frameMap.ContainsKey(7));
        long dataOffset = frameMap[7];
        var readBack = walData.AsSpan((int)dataOffset, PageSize);

        Assert.Equal(originalPage, readBack.ToArray());
    }

    // ── Multiple transactions ──

    [Fact]
    public void AppendFrame_TwoTransactions_BothCommitted()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();

        // Transaction 1
        writer.AppendFrame(1, MakePageData(0x01));
        writer.AppendCommitFrame(2, MakePageData(0x02), 5);

        // Transaction 2
        writer.AppendFrame(3, MakePageData(0x03));
        writer.AppendCommitFrame(1, MakePageData(0x11), 8); // overwrites page 1

        var walData = stream.ToArray();
        var frameMap = WalReader.ReadFrameMap(walData, PageSize);

        Assert.Equal(3, frameMap.Count);
        // Page 1 should have the later transaction's data
        long page1Offset = frameMap[1];
        Assert.Equal(0x11, walData[(int)page1Offset]);
    }

    // ── WAL file size ──

    [Fact]
    public void WriteHeader_ThenFrame_CorrectTotalSize()
    {
        using var stream = new MemoryStream();
        using var writer = new WalWriter(stream, PageSize);
        writer.WriteHeader();
        writer.AppendCommitFrame(1, MakePageData(0xAA), 5);

        int expected = WalHeader.HeaderSize + WalFrameHeader.HeaderSize + PageSize;
        Assert.Equal(expected, stream.Length);
    }

    // ── Helpers ──

    private static byte[] MakePageData(byte fillByte)
    {
        var data = new byte[PageSize];
        Array.Fill(data, fillByte);
        return data;
    }
}
