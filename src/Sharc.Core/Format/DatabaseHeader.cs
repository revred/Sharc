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
/// Parses the 100-byte SQLite database file header.
/// All multi-byte integers in the header are big-endian.
/// </summary>
public readonly struct DatabaseHeader
{
    /// <summary>Expected magic string at offset 0.</summary>
    public static ReadOnlySpan<byte> MagicBytes => "SQLite format 3\0"u8;

    /// <summary>Page size in bytes. Value 1 means 65536.</summary>
    public int PageSize { get; }

    /// <summary>File format write version (1=legacy, 2=WAL).</summary>
    public byte WriteVersion { get; }

    /// <summary>File format read version (1=legacy, 2=WAL).</summary>
    public byte ReadVersion { get; }

    /// <summary>Reserved bytes at end of each page.</summary>
    public byte ReservedBytesPerPage { get; }

    /// <summary>File change counter.</summary>
    public uint ChangeCounter { get; }

    /// <summary>Database size in pages.</summary>
    public int PageCount { get; }

    /// <summary>First freelist trunk page.</summary>
    public uint FirstFreelistPage { get; }

    /// <summary>Total number of freelist pages.</summary>
    public int FreelistPageCount { get; }

    /// <summary>Schema cookie (incremented on schema change).</summary>
    public uint SchemaCookie { get; }

    /// <summary>Schema format number (1â€“4).</summary>
    public int SchemaFormat { get; }

    /// <summary>Text encoding (1=UTF-8, 2=UTF-16le, 3=UTF-16be).</summary>
    public int TextEncoding { get; }

    /// <summary>User version integer.</summary>
    public int UserVersion { get; }

    /// <summary>Application ID.</summary>
    public int ApplicationId { get; }

    /// <summary>SQLite version number that created the database.</summary>
    public int SqliteVersionNumber { get; }

    /// <summary>Usable page size (PageSize - ReservedBytesPerPage).</summary>
    public int UsablePageSize => PageSize - ReservedBytesPerPage;

    /// <summary>Whether the database uses WAL journal mode.</summary>
    public bool IsWalMode => ReadVersion == 2 || WriteVersion == 2;

    /// <summary>
    /// Initializes a new database header.
    /// </summary>
    public DatabaseHeader(int pageSize, byte writeVersion, byte readVersion,
        byte reservedBytesPerPage, uint changeCounter, int pageCount,
        uint firstFreelistPage, int freelistPageCount, uint schemaCookie,
        int schemaFormat, int textEncoding, int userVersion,
        int applicationId, int sqliteVersionNumber)
    {
        PageSize = pageSize;
        WriteVersion = writeVersion;
        ReadVersion = readVersion;
        ReservedBytesPerPage = reservedBytesPerPage;
        ChangeCounter = changeCounter;
        PageCount = pageCount;
        FirstFreelistPage = firstFreelistPage;
        FreelistPageCount = freelistPageCount;
        SchemaCookie = schemaCookie;
        SchemaFormat = schemaFormat;
        TextEncoding = textEncoding;
        UserVersion = userVersion;
        ApplicationId = applicationId;
        SqliteVersionNumber = sqliteVersionNumber;
    }

    /// <summary>
    /// Parses a database header from the first 100 bytes of a database file.
    /// </summary>
    /// <param name="data">At least 100 bytes from the start of the database.</param>
    /// <returns>Parsed header.</returns>
    /// <exception cref="InvalidDatabaseException">Invalid magic or header values.</exception>
    public static DatabaseHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 100)
            throw new InvalidDatabaseException("Database header must be at least 100 bytes.");

        if (!data[..16].SequenceEqual(MagicBytes))
            throw new InvalidDatabaseException("Invalid SQLite magic string.");

        int rawPageSize = BinaryPrimitives.ReadUInt16BigEndian(data[16..]);
        int pageSize = rawPageSize == 1 ? 65536 : rawPageSize;

        return new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: data[18],
            readVersion: data[19],
            reservedBytesPerPage: data[20],
            changeCounter: BinaryPrimitives.ReadUInt32BigEndian(data[24..]),
            pageCount: (int)BinaryPrimitives.ReadUInt32BigEndian(data[28..]),
            firstFreelistPage: BinaryPrimitives.ReadUInt32BigEndian(data[32..]),
            freelistPageCount: (int)BinaryPrimitives.ReadUInt32BigEndian(data[36..]),
            schemaCookie: BinaryPrimitives.ReadUInt32BigEndian(data[40..]),
            schemaFormat: (int)BinaryPrimitives.ReadUInt32BigEndian(data[44..]),
            textEncoding: (int)BinaryPrimitives.ReadUInt32BigEndian(data[56..]),
            userVersion: (int)BinaryPrimitives.ReadUInt32BigEndian(data[60..]),
            applicationId: (int)BinaryPrimitives.ReadUInt32BigEndian(data[68..]),
            sqliteVersionNumber: (int)BinaryPrimitives.ReadUInt32BigEndian(data[96..])
        );
    }

    /// <summary>
    /// Validates the magic string without fully parsing the header.
    /// </summary>
    public static bool HasValidMagic(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16) return false;
        return data[..16].SequenceEqual(MagicBytes);
    }

    /// <summary>
    /// Writes the 100-byte database header to the destination span.
    /// </summary>
    /// <param name="destination">At least 100 bytes.</param>
    /// <param name="header">The header values to write.</param>
    public static void Write(Span<byte> destination, DatabaseHeader header)
    {
        destination[..100].Clear();
        MagicBytes.CopyTo(destination);

        ushort rawPageSize = header.PageSize == 65536 ? (ushort)1 : (ushort)header.PageSize;
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..], rawPageSize);
        destination[18] = header.WriteVersion;
        destination[19] = header.ReadVersion;
        destination[20] = header.ReservedBytesPerPage;
        destination[21] = 64;  // max embedded payload fraction (fixed)
        destination[22] = 32;  // min embedded payload fraction (fixed)
        destination[23] = 32;  // leaf payload fraction (fixed)
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..], header.ChangeCounter);
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..], (uint)header.PageCount);
        BinaryPrimitives.WriteUInt32BigEndian(destination[32..], header.FirstFreelistPage);
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..], (uint)header.FreelistPageCount);
        BinaryPrimitives.WriteUInt32BigEndian(destination[40..], header.SchemaCookie);
        BinaryPrimitives.WriteUInt32BigEndian(destination[44..], (uint)header.SchemaFormat);
        BinaryPrimitives.WriteUInt32BigEndian(destination[56..], (uint)header.TextEncoding);
        BinaryPrimitives.WriteUInt32BigEndian(destination[60..], (uint)header.UserVersion);
        BinaryPrimitives.WriteUInt32BigEndian(destination[68..], (uint)header.ApplicationId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[96..], (uint)header.SqliteVersionNumber);
    }
}
