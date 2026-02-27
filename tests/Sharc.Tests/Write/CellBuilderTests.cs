// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.BTree;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for CellBuilder — the write-side inverse of CellParser.
/// </summary>
public class CellBuilderTests
{
    private const int DefaultPageSize = 4096;

    // ── Table leaf cell: small record ──

    [Fact]
    public void BuildTableLeafCell_SmallRecord_RoundTrips()
    {
        // Small 10-byte record payload, rowid = 1
        byte[] payload = new byte[10];
        payload[0] = 2; // header size
        payload[1] = 0; // serial type NULL
        // rest is body

        var buffer = new byte[64];
        int written = CellBuilder.BuildTableLeafCell(1, payload, buffer, DefaultPageSize);

        // Parse it back
        int headerLen = CellParser.ParseTableLeafCell(buffer, out int parsedPayloadSize, out long parsedRowId);
        Assert.Equal(payload.Length, parsedPayloadSize);
        Assert.Equal(1L, parsedRowId);

        // Payload should match
        Assert.Equal(payload, buffer.AsSpan(headerLen, parsedPayloadSize).ToArray());
    }

    [Fact]
    public void BuildTableLeafCell_RowId1_WritesMinimalVarint()
    {
        byte[] payload = [0x02, 0x00]; // 2-byte record: header-size=2, serial type=0 (NULL)
        var buffer = new byte[64];
        int written = CellBuilder.BuildTableLeafCell(1, payload, buffer, DefaultPageSize);

        // payload-size varint (1 byte: value 2) + rowid varint (1 byte: value 1) + payload (2 bytes) = 4
        Assert.Equal(4, written);
    }

    [Fact]
    public void BuildTableLeafCell_LargeRowId_WritesMultiByteVarint()
    {
        byte[] payload = [0x02, 0x00];
        var buffer = new byte[64];
        long largeRowId = 1_000_000L;
        int written = CellBuilder.BuildTableLeafCell(largeRowId, payload, buffer, DefaultPageSize);

        int headerLen = CellParser.ParseTableLeafCell(buffer, out int parsedPayloadSize, out long parsedRowId);
        Assert.Equal(largeRowId, parsedRowId);
        Assert.Equal(payload.Length, parsedPayloadSize);
    }

    // ── Table leaf cell: overflow ──

    [Fact]
    public void BuildTableLeafCell_OverflowRecord_HasOverflowPointer()
    {
        // Create a record larger than inline payload size
        // For page size 4096: X = 4096 - 35 = 4061
        // A record of 4100 bytes will overflow
        byte[] payload = new byte[4100];
        payload[0] = 2;
        payload[1] = 0;

        var buffer = new byte[8192];
        int written = CellBuilder.BuildTableLeafCell(1, payload, buffer, DefaultPageSize);

        // Parse cell header
        int headerLen = CellParser.ParseTableLeafCell(buffer, out int parsedPayloadSize, out long parsedRowId);
        Assert.Equal(payload.Length, parsedPayloadSize);
        Assert.Equal(1L, parsedRowId);

        // Inline payload should be less than total payload
        int inlineSize = CellParser.CalculateInlinePayloadSize(payload.Length, DefaultPageSize);
        Assert.True(inlineSize < payload.Length);

        // Last 4 bytes of cell should be the overflow page pointer (0 since we don't know the page)
        int expectedCellSize = headerLen + inlineSize + 4; // +4 for overflow pointer
        Assert.Equal(expectedCellSize, written);
    }

    [Fact]
    public void BuildTableLeafCell_ExactlyAtThreshold_NoOverflow()
    {
        // X = 4096 - 35 = 4061; record of exactly 4061 bytes should NOT overflow
        int x = DefaultPageSize - 35;
        byte[] payload = new byte[x];
        payload[0] = 2;
        payload[1] = 0;

        var buffer = new byte[8192];
        int written = CellBuilder.BuildTableLeafCell(1, payload, buffer, DefaultPageSize);

        int headerLen = CellParser.ParseTableLeafCell(buffer, out int parsedPayloadSize, out long parsedRowId);
        // No overflow → cell size = header + full payload (no 4-byte overflow pointer)
        Assert.Equal(headerLen + payload.Length, written);
    }

    // ── Table interior cell ──

    [Fact]
    public void BuildTableInteriorCell_Standard_RoundTrips()
    {
        var buffer = new byte[64];
        int written = CellBuilder.BuildTableInteriorCell(42, 100, buffer);

        int consumed = CellParser.ParseTableInteriorCell(buffer, out uint leftChild, out long rowId);
        Assert.Equal(42u, leftChild);
        Assert.Equal(100L, rowId);
        Assert.Equal(consumed, written);
    }

    [Fact]
    public void BuildTableInteriorCell_LargeRowId_RoundTrips()
    {
        var buffer = new byte[64];
        long largeRowId = long.MaxValue;
        int written = CellBuilder.BuildTableInteriorCell(999, largeRowId, buffer);

        CellParser.ParseTableInteriorCell(buffer, out uint leftChild, out long rowId);
        Assert.Equal(999u, leftChild);
        Assert.Equal(largeRowId, rowId);
    }

    [Fact]
    public void BuildTableInteriorCell_RowIdOne_MinimalSize()
    {
        var buffer = new byte[64];
        int written = CellBuilder.BuildTableInteriorCell(1, 1, buffer);

        // 4 bytes (child page) + 1 byte (rowid varint for value 1) = 5
        Assert.Equal(5, written);
    }

    // ── ComputeTableLeafCellSize ──

    [Fact]
    public void ComputeTableLeafCellSize_SmallRecord_MatchesActual()
    {
        byte[] payload = new byte[50];
        long rowId = 42;
        int computed = CellBuilder.ComputeTableLeafCellSize(rowId, payload.Length, DefaultPageSize);

        var buffer = new byte[256];
        int actual = CellBuilder.BuildTableLeafCell(rowId, payload, buffer, DefaultPageSize);
        Assert.Equal(actual, computed);
    }

    [Fact]
    public void ComputeTableLeafCellSize_OverflowRecord_MatchesActual()
    {
        byte[] payload = new byte[5000];
        long rowId = 1;
        int computed = CellBuilder.ComputeTableLeafCellSize(rowId, payload.Length, DefaultPageSize);

        var buffer = new byte[8192];
        int actual = CellBuilder.BuildTableLeafCell(rowId, payload, buffer, DefaultPageSize);
        Assert.Equal(actual, computed);
    }

    [Fact]
    public void ComputeTableLeafCellSize_LargeRowId_MatchesActual()
    {
        byte[] payload = new byte[20];
        long rowId = 9_999_999_999L;
        int computed = CellBuilder.ComputeTableLeafCellSize(rowId, payload.Length, DefaultPageSize);

        var buffer = new byte[256];
        int actual = CellBuilder.BuildTableLeafCell(rowId, payload, buffer, DefaultPageSize);
        Assert.Equal(actual, computed);
    }

    // ── Inline payload bytes match ──

    [Fact]
    public void BuildTableLeafCell_InlinePayloadMatchesOriginal()
    {
        // Create a recognizable payload pattern
        byte[] payload = new byte[100];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i & 0xFF);

        var buffer = new byte[256];
        int written = CellBuilder.BuildTableLeafCell(7, payload, buffer, DefaultPageSize);

        int headerLen = CellParser.ParseTableLeafCell(buffer, out int parsedPayloadSize, out long parsedRowId);
        Assert.Equal(7L, parsedRowId);

        // Verify inline bytes match the original payload
        var inlineBytes = buffer.AsSpan(headerLen, parsedPayloadSize);
        Assert.Equal(payload, inlineBytes.ToArray());
    }
}