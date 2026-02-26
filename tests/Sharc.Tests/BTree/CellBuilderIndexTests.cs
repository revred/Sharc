// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.BTree;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

public sealed class CellBuilderIndexTests
{
    private const int UsablePageSize = 4096;

    [Fact]
    public void BuildIndexLeafCell_SmallPayload_CorrectFormat()
    {
        // Build a simple index record: header(3) + serialType(1) + serialType(1) + key(1) + rowid(1) = 5 bytes
        byte[] payload = [0x03, 0x01, 0x01, 0x42, 0x01];

        Span<byte> dest = stackalloc byte[64];
        int written = CellBuilder.BuildIndexLeafCell(payload, dest, UsablePageSize);

        // Cell format: [payloadSize:varint] [payload]
        // payloadSize = 5 -> varint = 1 byte (0x05)
        // Total = 1 + 5 = 6
        Assert.Equal(6, written);

        // Verify payload size varint
        int offset = VarintDecoder.Read(dest, out long payloadSize);
        Assert.Equal(5, payloadSize);
        Assert.Equal(1, offset);

        // Verify payload bytes
        Assert.True(dest.Slice(offset, 5).SequenceEqual(payload));
    }

    [Fact]
    public void ComputeIndexLeafCellSize_MatchesBuildOutput()
    {
        byte[] payload = [0x03, 0x01, 0x01, 0x42, 0x01];

        int computed = CellBuilder.ComputeIndexLeafCellSize(payload.Length, UsablePageSize);

        Span<byte> dest = stackalloc byte[64];
        int actual = CellBuilder.BuildIndexLeafCell(payload, dest, UsablePageSize);

        Assert.Equal(actual, computed);
    }

    [Fact]
    public void BuildIndexLeafCell_LargePayload_WritesOverflowPointer()
    {
        // Index inline threshold for 4096-byte page:
        // X = ((4096-12)*64/255) - 23 = 1001
        // Use a payload > 1001 bytes to trigger overflow
        byte[] payload = new byte[1100];
        payload[0] = 0x02; // minimal record header
        payload[1] = 0x00; // NULL serial type

        Span<byte> dest = stackalloc byte[2048];
        int written = CellBuilder.BuildIndexLeafCell(payload, dest, UsablePageSize);

        // Should have: varint(1100) + inline portion + 4-byte overflow pointer
        int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(1100, UsablePageSize);
        Assert.True(inlineSize < 1100);

        int expected = VarintDecoder.GetEncodedLength(1100) + inlineSize + 4;
        Assert.Equal(expected, written);

        // Verify the overflow pointer is at the end (initially 0)
        var overflowPtr = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            dest.Slice(written - 4, 4));
        Assert.Equal(0u, overflowPtr);
    }

    // ── Index Interior Cell tests ──────────────────────────────────────

    [Fact]
    public void BuildIndexInteriorCell_SmallPayload_CorrectFormat()
    {
        // Index interior cell: [leftChild:4-BE] [payloadSize:varint] [payload]
        uint leftChild = 42;
        byte[] payload = [0x03, 0x01, 0x01, 0x42, 0x01]; // 5-byte payload

        Span<byte> dest = stackalloc byte[64];
        int written = CellBuilder.BuildIndexInteriorCell(leftChild, payload, dest, UsablePageSize);

        // Total = 4 (leftChild) + 1 (varint for payload size 5) + 5 (payload) = 10
        Assert.Equal(10, written);

        // Verify left child page
        uint readChild = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dest);
        Assert.Equal(42u, readChild);

        // Verify payload size varint
        int offset = VarintDecoder.Read(dest[4..], out long payloadSize);
        Assert.Equal(5, payloadSize);

        // Verify payload bytes
        Assert.True(dest.Slice(4 + offset, 5).SequenceEqual(payload));
    }

    [Fact]
    public void ComputeIndexInteriorCellSize_MatchesBuildOutput()
    {
        byte[] payload = [0x03, 0x01, 0x01, 0x42, 0x01];

        int computed = CellBuilder.ComputeIndexInteriorCellSize(payload.Length, UsablePageSize);

        Span<byte> dest = stackalloc byte[64];
        int actual = CellBuilder.BuildIndexInteriorCell(42, payload, dest, UsablePageSize);

        Assert.Equal(actual, computed);
    }
}
