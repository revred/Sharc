// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers.Binary;

namespace Sharc.Core.Format;

/// <summary>
/// Parses the 24-byte WAL frame header that precedes each page in the WAL file.
/// </summary>
/// <remarks>
/// Frame header layout:
/// <code>
/// Offset  Size  Field
///   0      4    Page number
///   4      4    DB size after commit (0 = non-commit frame)
///   8      4    Salt-1
///  12      4    Salt-2
///  16      4    Checksum-1
///  20      4    Checksum-2
/// </code>
/// </remarks>
public readonly struct WalFrameHeader
{
    /// <summary>Frame header size in bytes.</summary>
    public const int HeaderSize = 24;

    /// <summary>Page number that this frame replaces in the database file.</summary>
    public uint PageNumber { get; }

    /// <summary>
    /// Database size in pages after the commit that includes this frame.
    /// Zero if this frame is not a commit frame.
    /// </summary>
    public uint DbSizeAfterCommit { get; }

    /// <summary>Salt value 1 (must match WAL header for frame to be valid).</summary>
    public uint Salt1 { get; }

    /// <summary>Salt value 2 (must match WAL header for frame to be valid).</summary>
    public uint Salt2 { get; }

    /// <summary>Cumulative checksum part 1.</summary>
    public uint Checksum1 { get; }

    /// <summary>Cumulative checksum part 2.</summary>
    public uint Checksum2 { get; }

    /// <summary>
    /// Whether this frame marks the end of a transaction.
    /// A commit frame has a non-zero DbSizeAfterCommit.
    /// </summary>
    public bool IsCommitFrame => DbSizeAfterCommit != 0;

    private WalFrameHeader(uint pageNumber, uint dbSizeAfterCommit,
        uint salt1, uint salt2, uint checksum1, uint checksum2)
    {
        PageNumber = pageNumber;
        DbSizeAfterCommit = dbSizeAfterCommit;
        Salt1 = salt1;
        Salt2 = salt2;
        Checksum1 = checksum1;
        Checksum2 = checksum2;
    }

    /// <summary>
    /// Parses a WAL frame header from a 24-byte span.
    /// </summary>
    /// <param name="data">At least 24 bytes of frame header data.</param>
    /// <returns>Parsed frame header.</returns>
    /// <exception cref="ArgumentException">Insufficient data.</exception>
    public static WalFrameHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new ArgumentException($"Frame header must be at least {HeaderSize} bytes.");

        return new WalFrameHeader(
            pageNumber: BinaryPrimitives.ReadUInt32BigEndian(data),
            dbSizeAfterCommit: BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
            salt1: BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
            salt2: BinaryPrimitives.ReadUInt32BigEndian(data[12..]),
            checksum1: BinaryPrimitives.ReadUInt32BigEndian(data[16..]),
            checksum2: BinaryPrimitives.ReadUInt32BigEndian(data[20..])
        );
    }

    /// <summary>
    /// Writes the 24-byte WAL frame header to the destination span.
    /// </summary>
    /// <param name="destination">At least 24 bytes.</param>
    /// <param name="header">The header values to write.</param>
    public static void Write(Span<byte> destination, WalFrameHeader header)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, header.PageNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], header.DbSizeAfterCommit);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], header.Salt1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], header.Salt2);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..], header.Checksum1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..], header.Checksum2);
    }
}