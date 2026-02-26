/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Writes WAL (Write-Ahead Log) frames to a stream.
/// Produces output that is fully readable by <see cref="WalReader"/>.
/// </summary>
internal sealed class WalWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly int _pageSize;
    private readonly bool _bigEndianChecksums;
    private uint _salt1;
    private uint _salt2;
    private uint _s0; // cumulative checksum part 1
    private uint _s1; // cumulative checksum part 2
    private bool _disposed;

    /// <summary>
    /// Creates a new WAL writer.
    /// </summary>
    /// <param name="stream">The output stream (must be writable and seekable).</param>
    /// <param name="pageSize">Database page size in bytes.</param>
    /// <param name="bigEndianChecksums">
    /// If true, use big-endian checksums (magic 0x377F0683).
    /// Default false uses native byte order (magic 0x377F0682, little-endian on x86).
    /// </param>
    public WalWriter(Stream stream, int pageSize, bool bigEndianChecksums = false)
    {
        _stream = stream;
        _pageSize = pageSize;
        _bigEndianChecksums = bigEndianChecksums;
        // Generate deterministic salts based on initial stream position
        _salt1 = 0x01020304;
        _salt2 = 0x05060708;
    }

    /// <summary>
    /// Writes the 32-byte WAL header. Must be called before any AppendFrame calls.
    /// </summary>
    public void WriteHeader()
    {
        Span<byte> header = stackalloc byte[WalHeader.HeaderSize];

        uint magic = _bigEndianChecksums ? WalHeader.MagicLittleEndian : WalHeader.MagicBigEndian;
        BinaryPrimitives.WriteUInt32BigEndian(header, magic);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], 3007000); // format version
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)_pageSize);
        BinaryPrimitives.WriteUInt32BigEndian(header[12..], 0); // checkpoint seq
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], _salt1);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], _salt2);

        // Compute checksum over the first 24 bytes (everything except checksum fields)
        _s0 = 0;
        _s1 = 0;
        WalReader.ComputeChecksum(header[..24], _bigEndianChecksums, ref _s0, ref _s1);

        BinaryPrimitives.WriteUInt32BigEndian(header[24..], _s0);
        BinaryPrimitives.WriteUInt32BigEndian(header[28..], _s1);

        _stream.Write(header);
    }

    /// <summary>
    /// Appends a non-commit frame to the WAL.
    /// </summary>
    /// <param name="pageNumber">The database page number this frame replaces.</param>
    /// <param name="pageData">The full page data. Must be exactly <see cref="_pageSize"/> bytes.</param>
    public void AppendFrame(uint pageNumber, ReadOnlySpan<byte> pageData)
    {
        WriteFrame(pageNumber, pageData, dbSizeAfterCommit: 0);
    }

    /// <summary>
    /// Appends a commit frame to the WAL, marking the end of a transaction.
    /// </summary>
    /// <param name="pageNumber">The database page number this frame replaces.</param>
    /// <param name="pageData">The full page data. Must be exactly <see cref="_pageSize"/> bytes.</param>
    /// <param name="dbSizeInPages">Database size in pages after this commit.</param>
    public void AppendCommitFrame(uint pageNumber, ReadOnlySpan<byte> pageData, uint dbSizeInPages)
    {
        WriteFrame(pageNumber, pageData, dbSizeAfterCommit: dbSizeInPages);
    }

    /// <summary>
    /// Flushes the stream.
    /// </summary>
    public void Sync()
    {
        _stream.Flush();
    }

    private void WriteFrame(uint pageNumber, ReadOnlySpan<byte> pageData, uint dbSizeAfterCommit)
    {
        Span<byte> frameHeader = stackalloc byte[WalFrameHeader.HeaderSize];

        BinaryPrimitives.WriteUInt32BigEndian(frameHeader, pageNumber);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[4..], dbSizeAfterCommit);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[8..], _salt1);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[12..], _salt2);

        // Cumulative checksum: first 8 bytes of frame header + page data
        WalReader.ComputeChecksum(frameHeader[..8], _bigEndianChecksums, ref _s0, ref _s1);
        WalReader.ComputeChecksum(pageData, _bigEndianChecksums, ref _s0, ref _s1);

        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[16..], _s0);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[20..], _s1);

        _stream.Write(frameHeader);
        _stream.Write(pageData);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Don't dispose the stream — caller owns it
    }
}
