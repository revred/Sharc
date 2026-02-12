/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Exceptions;

namespace Sharc.Core.Format;

/// <summary>
/// Parses the 32-byte WAL (Write-Ahead Log) file header.
/// </summary>
/// <remarks>
/// WAL header layout:
/// <code>
/// Offset  Size  Field
///   0      4    Magic number (0x377F0682 = big-endian checksums, 0x377F0683 = little-endian)
///   4      4    File format version (3007000)
///   8      4    Database page size
///  12      4    Checkpoint sequence number
///  16      4    Salt-1
///  20      4    Salt-2
///  24      4    Checksum-1
///  28      4    Checksum-2
/// </code>
/// </remarks>
public readonly struct WalHeader
{
    /// <summary>Magic number indicating big-endian checksum mode.</summary>
    public const uint MagicBigEndian = 0x377F0682;

    /// <summary>Magic number indicating little-endian (native) checksum mode.</summary>
    public const uint MagicLittleEndian = 0x377F0683;

    /// <summary>WAL header size in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>Magic number from the WAL header.</summary>
    public uint Magic { get; }

    /// <summary>File format version (expected: 3007000).</summary>
    public uint FormatVersion { get; }

    /// <summary>Database page size in bytes.</summary>
    public int PageSize { get; }

    /// <summary>Checkpoint sequence number.</summary>
    public uint CheckpointSequence { get; }

    /// <summary>Salt value 1.</summary>
    public uint Salt1 { get; }

    /// <summary>Salt value 2.</summary>
    public uint Salt2 { get; }

    /// <summary>Checksum part 1 (cumulative checksum of the header).</summary>
    public uint Checksum1 { get; }

    /// <summary>Checksum part 2 (cumulative checksum of the header).</summary>
    public uint Checksum2 { get; }

    /// <summary>
    /// Whether the WAL uses native byte order for checksums.
    /// Magic 0x377F0682 (bit 0 clear) means the WAL was created on a native-byte-order
    /// machine with native checksum reads. On x86/LE, this means little-endian checksums.
    /// Magic 0x377F0683 (bit 0 set) means big-endian checksum flag was set by the creator.
    /// </summary>
    public bool IsNativeByteOrder => Magic == MagicBigEndian;

    private WalHeader(uint magic, uint formatVersion, int pageSize,
        uint checkpointSequence, uint salt1, uint salt2,
        uint checksum1, uint checksum2)
    {
        Magic = magic;
        FormatVersion = formatVersion;
        PageSize = pageSize;
        CheckpointSequence = checkpointSequence;
        Salt1 = salt1;
        Salt2 = salt2;
        Checksum1 = checksum1;
        Checksum2 = checksum2;
    }

    /// <summary>
    /// Parses a WAL header from the first 32 bytes of a WAL file.
    /// </summary>
    /// <param name="data">At least 32 bytes from the start of the WAL file.</param>
    /// <returns>Parsed WAL header.</returns>
    /// <exception cref="InvalidDatabaseException">Invalid magic or insufficient data.</exception>
    public static WalHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDatabaseException("WAL header must be at least 32 bytes.");

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (magic != MagicBigEndian && magic != MagicLittleEndian)
            throw new InvalidDatabaseException($"Invalid WAL magic number: 0x{magic:X8}.");

        return new WalHeader(
            magic: magic,
            formatVersion: BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
            pageSize: (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
            checkpointSequence: BinaryPrimitives.ReadUInt32BigEndian(data[12..]),
            salt1: BinaryPrimitives.ReadUInt32BigEndian(data[16..]),
            salt2: BinaryPrimitives.ReadUInt32BigEndian(data[20..]),
            checksum1: BinaryPrimitives.ReadUInt32BigEndian(data[24..]),
            checksum2: BinaryPrimitives.ReadUInt32BigEndian(data[28..])
        );
    }
}
