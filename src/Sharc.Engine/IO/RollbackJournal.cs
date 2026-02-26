// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Exceptions;

namespace Sharc.Core.IO;

/// <summary>
/// Manages the rollback journal file used for achieving crash-atomicity.
/// When a transaction is committed, original page contents are stored here first.
/// If a crash occurs, Sharc can reconstruct the pre-transaction state from this journal.
/// </summary>
internal static class RollbackJournal
{
    private static ReadOnlySpan<byte> Magic => "SHARC_RJ"u8;
    private const int HeaderSize = 16;

    /// <summary>
    /// Creates a journal file and writes the header and all original page contents.
    /// </summary>
    public static void CreateJournal(string journalPath, IPageSource baseSource, IEnumerable<uint> dirtyPageNumbers)
    {
        using var fs = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1);
        
        // Header
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header[8..], baseSource.PageSize);
        BinaryPrimitives.WriteInt32BigEndian(header[12..], baseSource.PageCount);
        fs.Write(header);

        // Frames
        Span<byte> frameHeader = stackalloc byte[4];
        foreach (var pageNum in dirtyPageNumbers)
        {
            // Skip pages that are newly allocated and don't exist in the base file yet.
            // Rolling back will simply truncate the file to the original size.
            if (pageNum > baseSource.PageCount) continue;

            BinaryPrimitives.WriteUInt32BigEndian(frameHeader, pageNum);
            fs.Write(frameHeader);
            fs.Write(baseSource.GetPage(pageNum));
        }

        fs.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Recovers a database from a journal file.
    /// </summary>
    public static void Recover(string databasePath, string journalPath)
    {
        using var journalFs = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.None);
        if (journalFs.Length < HeaderSize) return;

        Span<byte> header = stackalloc byte[HeaderSize];
        journalFs.ReadExactly(header);

        if (!header[..8].SequenceEqual(Magic))
            throw new InvalidDatabaseException("Invalid rollback journal magic string.");

        int pageSize = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
        int originalPageCount = BinaryPrimitives.ReadInt32BigEndian(header[12..]);

        using var dbFs = new FileStream(databasePath, FileMode.Open, FileAccess.Write, FileShare.None);
        
        byte[] pageBuffer = new byte[pageSize];
        Span<byte> frameHeader = stackalloc byte[4];

        while (journalFs.Position < journalFs.Length)
        {
            journalFs.ReadExactly(frameHeader);
            uint pageNum = BinaryPrimitives.ReadUInt32BigEndian(frameHeader);
            journalFs.ReadExactly(pageBuffer);

            long offset = (long)(pageNum - 1) * pageSize;
            dbFs.Seek(offset, SeekOrigin.Begin);
            dbFs.Write(pageBuffer);
        }

        // Restore original page count if needed (truncate if DB grew during failed commit)
        dbFs.SetLength((long)originalPageCount * pageSize);
        dbFs.Flush(flushToDisk: true);
    }
}
