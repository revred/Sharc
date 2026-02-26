// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers.Binary;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Reads a SQLite WAL (Write-Ahead Log) file and builds a mapping of page numbers
/// to their latest committed frame offsets within the WAL data.
/// </summary>
/// <remarks>
/// The WAL reader validates:
/// <list type="bullet">
/// <item>Salt values match the WAL header for each frame</item>
/// <item>Cumulative checksums are correct across all frames</item>
/// <item>Only frames from committed transactions are included</item>
/// </list>
/// When the same page appears multiple times, the latest committed version wins.
/// </remarks>
public static class WalReader
{
    /// <summary>
    /// Reads all valid committed frames from the WAL data and returns a mapping
    /// of page numbers to their data offsets within the WAL buffer.
    /// </summary>
    /// <param name="walData">The complete WAL file contents.</param>
    /// <param name="pageSize">Expected database page size in bytes.</param>
    /// <returns>
    /// Dictionary mapping page numbers to byte offsets within <paramref name="walData"/>
    /// where the page data begins (after the frame header).
    /// </returns>
    public static Dictionary<uint, long> ReadFrameMap(ReadOnlyMemory<byte> walData, int pageSize)
    {
        var span = walData.Span;

        if (span.Length < WalHeader.HeaderSize)
            return new Dictionary<uint, long>();

        var walHeader = WalHeader.Parse(span);
        bool bigEndian = !walHeader.IsNativeByteOrder;

        int frameSize = WalFrameHeader.HeaderSize + pageSize;
        int frameCount = (span.Length - WalHeader.HeaderSize) / frameSize;
        if (frameCount == 0)
            return new Dictionary<uint, long>();

        // Start with the WAL header checksum as the initial cumulative checksum
        uint s0 = walHeader.Checksum1;
        uint s1 = walHeader.Checksum2;

        // Pending frames from the current uncommitted transaction
        var pendingFrames = new List<(uint pageNumber, long dataOffset)>();
        // Committed frame map â€” latest committed offset per page
        var committedMap = new Dictionary<uint, long>();

        int offset = WalHeader.HeaderSize;
        for (int i = 0; i < frameCount; i++)
        {
            if (offset + frameSize > span.Length)
                break;

            var frameHeader = WalFrameHeader.Parse(span[offset..]);

            // Validate salt matches WAL header
            if (frameHeader.Salt1 != walHeader.Salt1 || frameHeader.Salt2 != walHeader.Salt2)
                break; // Invalid frame â€” stop processing

            // Compute cumulative checksum: frame header first 8 bytes + page data
            ComputeChecksum(span.Slice(offset, 8), bigEndian, ref s0, ref s1);
            int pageDataOffset = offset + WalFrameHeader.HeaderSize;
            ComputeChecksum(span.Slice(pageDataOffset, pageSize), bigEndian, ref s0, ref s1);

            // Validate checksum
            if (frameHeader.Checksum1 != s0 || frameHeader.Checksum2 != s1)
                break; // Checksum mismatch â€” stop processing

            // Frame is valid â€” add to pending
            pendingFrames.Add((frameHeader.PageNumber, pageDataOffset));

            // If this is a commit frame, promote all pending frames to committed
            if (frameHeader.IsCommitFrame)
            {
                foreach (var (pageNumber, dataOffset) in pendingFrames)
                {
                    committedMap[pageNumber] = dataOffset;
                }
                pendingFrames.Clear();
            }

            offset += frameSize;
        }

        // Discard any pending (uncommitted) frames
        return committedMap;
    }

    /// <summary>
    /// Computes the SQLite WAL cumulative checksum over a data span.
    /// The span length must be a multiple of 8.
    /// </summary>
    internal static void ComputeChecksum(ReadOnlySpan<byte> data, bool bigEndian,
        ref uint s0, ref uint s1)
    {
        for (int i = 0; i + 7 < data.Length; i += 8)
        {
            uint w0, w1;
            if (bigEndian)
            {
                w0 = BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
                w1 = BinaryPrimitives.ReadUInt32BigEndian(data[(i + 4)..]);
            }
            else
            {
                w0 = BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
                w1 = BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]);
            }

            s0 += w0 + s1;
            s1 += w1 + s0;
        }
    }
}