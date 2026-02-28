// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Core.Format;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for header Write methods — the write-side inverse of Parse.
/// </summary>
public class HeaderWriteTests
{
    // ══════════════════════════════════════════════════════════════
    //  DatabaseHeader
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void DatabaseHeader_Write_RoundTrips()
    {
        // Create a known valid 100-byte header, parse it, write it back, compare
        var original = BuildValidDatabaseHeader(pageSize: 4096, pageCount: 10,
            changeCounter: 5, schemaCookie: 3, textEncoding: 1);

        var parsed = DatabaseHeader.Parse(original);
        var written = new byte[100];
        DatabaseHeader.Write(written, parsed);

        // All fields we care about should round-trip
        var reparsed = DatabaseHeader.Parse(written);
        Assert.Equal(parsed.PageSize, reparsed.PageSize);
        Assert.Equal(parsed.WriteVersion, reparsed.WriteVersion);
        Assert.Equal(parsed.ReadVersion, reparsed.ReadVersion);
        Assert.Equal(parsed.ReservedBytesPerPage, reparsed.ReservedBytesPerPage);
        Assert.Equal(parsed.ChangeCounter, reparsed.ChangeCounter);
        Assert.Equal(parsed.PageCount, reparsed.PageCount);
        Assert.Equal(parsed.FirstFreelistPage, reparsed.FirstFreelistPage);
        Assert.Equal(parsed.FreelistPageCount, reparsed.FreelistPageCount);
        Assert.Equal(parsed.SchemaCookie, reparsed.SchemaCookie);
        Assert.Equal(parsed.SchemaFormat, reparsed.SchemaFormat);
        Assert.Equal(parsed.TextEncoding, reparsed.TextEncoding);
        Assert.Equal(parsed.UserVersion, reparsed.UserVersion);
        Assert.Equal(parsed.ApplicationId, reparsed.ApplicationId);
        Assert.Equal(parsed.SqliteVersionNumber, reparsed.SqliteVersionNumber);
    }

    [Fact]
    public void DatabaseHeader_Write_MagicStringCorrect()
    {
        var original = BuildValidDatabaseHeader();
        var parsed = DatabaseHeader.Parse(original);
        var written = new byte[100];
        DatabaseHeader.Write(written, parsed);

        Assert.True(written.AsSpan(0, 16).SequenceEqual(DatabaseHeader.MagicBytes));
    }

    [Fact]
    public void DatabaseHeader_Write_PageSize65536_WritesAs1()
    {
        var original = BuildValidDatabaseHeader(pageSize: 65536);
        var parsed = DatabaseHeader.Parse(original);
        var written = new byte[100];
        DatabaseHeader.Write(written, parsed);

        // Raw value at offset 16 should be 1 (special encoding for 65536)
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(written.AsSpan(16)));
    }

    [Fact]
    public void DatabaseHeader_Write_WalMode_WritesVersions()
    {
        var original = BuildValidDatabaseHeader();
        original[18] = 2; // writeVersion = WAL
        original[19] = 2; // readVersion = WAL
        var parsed = DatabaseHeader.Parse(original);

        var written = new byte[100];
        DatabaseHeader.Write(written, parsed);

        Assert.Equal(2, written[18]);
        Assert.Equal(2, written[19]);
        Assert.True(DatabaseHeader.Parse(written).IsWalMode);
    }

    // ══════════════════════════════════════════════════════════════
    //  BTreePageHeader
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void BTreePageHeader_Write_LeafTable_RoundTrips()
    {
        var original = new byte[12];
        original[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(1), 0); // freeblock
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(3), 42); // cellCount
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(5), 1000); // cellContentOffset
        original[7] = 3; // fragmented bytes

        var parsed = BTreePageHeader.Parse(original);
        var written = new byte[12];
        BTreePageHeader.Write(written, parsed);

        var reparsed = BTreePageHeader.Parse(written);
        Assert.Equal(BTreePageType.LeafTable, reparsed.PageType);
        Assert.Equal((ushort)0, reparsed.FirstFreeblockOffset);
        Assert.Equal((ushort)42, reparsed.CellCount);
        Assert.Equal((ushort)1000, reparsed.CellContentOffset);
        Assert.Equal((byte)3, reparsed.FragmentedFreeBytes);
        Assert.Equal(0u, reparsed.RightChildPage);
    }

    [Fact]
    public void BTreePageHeader_Write_InteriorTable_RoundTrips()
    {
        var original = new byte[12];
        original[0] = 0x05; // InteriorTable
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(1), 0);
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(3), 10);
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(5), 500);
        original[7] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(8), 99); // rightChild

        var parsed = BTreePageHeader.Parse(original);
        var written = new byte[12];
        BTreePageHeader.Write(written, parsed);

        var reparsed = BTreePageHeader.Parse(written);
        Assert.Equal(BTreePageType.InteriorTable, reparsed.PageType);
        Assert.Equal((ushort)10, reparsed.CellCount);
        Assert.Equal(99u, reparsed.RightChildPage);
    }

    [Fact]
    public void BTreePageHeader_Write_LeafIndex_RoundTrips()
    {
        var original = new byte[12];
        original[0] = 0x0A; // LeafIndex
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(3), 5);
        BinaryPrimitives.WriteUInt16BigEndian(original.AsSpan(5), 200);

        var parsed = BTreePageHeader.Parse(original);
        var written = new byte[12];
        BTreePageHeader.Write(written, parsed);

        var reparsed = BTreePageHeader.Parse(written);
        Assert.Equal(BTreePageType.LeafIndex, reparsed.PageType);
        Assert.Equal((ushort)5, reparsed.CellCount);
        Assert.True(reparsed.IsLeaf);
    }

    [Fact]
    public void BTreePageHeader_Write_ReturnsCorrectSize()
    {
        var leafBytes = new byte[12];
        leafBytes[0] = 0x0D;
        var leaf = BTreePageHeader.Parse(leafBytes);
        Assert.Equal(8, BTreePageHeader.Write(new byte[12], leaf));

        var interiorBytes = new byte[12];
        interiorBytes[0] = 0x05;
        var interior = BTreePageHeader.Parse(interiorBytes);
        Assert.Equal(12, BTreePageHeader.Write(new byte[12], interior));
    }

    // ══════════════════════════════════════════════════════════════
    //  WalHeader
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void WalHeader_Write_RoundTrips()
    {
        var original = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(original, WalHeader.MagicBigEndian);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(4), 3007000);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(8), 4096);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(12), 7); // checkpoint seq
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(16), 0xAABBCCDD); // salt1
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(20), 0x11223344); // salt2
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(24), 0x55667788); // cksum1
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(28), 0x99AABBCC); // cksum2

        var parsed = WalHeader.Parse(original);
        var written = new byte[32];
        WalHeader.Write(written, parsed);

        Assert.Equal(original, written);
    }

    [Fact]
    public void WalHeader_Write_LittleEndianMagic_RoundTrips()
    {
        var original = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(original, WalHeader.MagicLittleEndian);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(4), 3007000);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(8), 4096);

        var parsed = WalHeader.Parse(original);
        var written = new byte[32];
        WalHeader.Write(written, parsed);

        Assert.Equal(WalHeader.MagicLittleEndian, BinaryPrimitives.ReadUInt32BigEndian(written));
    }

    // ══════════════════════════════════════════════════════════════
    //  WalFrameHeader
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void WalFrameHeader_Write_RoundTrips()
    {
        var original = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(original, 5); // pageNumber
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(4), 100); // dbSize (commit frame)
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(8), 0xAAAA); // salt1
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(12), 0xBBBB); // salt2
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(16), 0xCCCC); // cksum1
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(20), 0xDDDD); // cksum2

        var parsed = WalFrameHeader.Parse(original);
        var written = new byte[24];
        WalFrameHeader.Write(written, parsed);

        Assert.Equal(original, written);
    }

    [Fact]
    public void WalFrameHeader_Write_NonCommitFrame_RoundTrips()
    {
        var original = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(original, 3); // pageNumber
        // dbSize = 0 → non-commit frame

        var parsed = WalFrameHeader.Parse(original);
        var written = new byte[24];
        WalFrameHeader.Write(written, parsed);

        var reparsed = WalFrameHeader.Parse(written);
        Assert.Equal(3u, reparsed.PageNumber);
        Assert.False(reparsed.IsCommitFrame);
    }

    // ── Helpers ──

    private static byte[] BuildValidDatabaseHeader(
        int pageSize = 4096, int pageCount = 1,
        uint changeCounter = 0, uint schemaCookie = 0,
        int textEncoding = 1)
    {
        var header = new byte[100];
        "SQLite format 3\0"u8.CopyTo(header);

        ushort rawPageSize = pageSize == 65536 ? (ushort)1 : (ushort)pageSize;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(16), rawPageSize);
        header[18] = 1; // writeVersion (legacy)
        header[19] = 1; // readVersion (legacy)
        header[20] = 0; // reservedBytesPerPage
        header[21] = 64; // max embedded payload fraction
        header[22] = 32; // min embedded payload fraction
        header[23] = 32; // leaf payload fraction
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24), changeCounter);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28), (uint)pageCount);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(40), schemaCookie);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(44), 4); // schema format
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(56), (uint)textEncoding);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(96), 3049000); // SQLite version
        return header;
    }
}