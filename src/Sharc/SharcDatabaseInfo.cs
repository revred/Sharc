// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Read-only metadata from the SQLite database header.
/// </summary>
public sealed record SharcDatabaseInfo
{
    /// <summary>Page size in bytes (512–65536).</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of pages in the database.</summary>
    public required int PageCount { get; init; }

    /// <summary>Text encoding (1=UTF-8, 2=UTF-16le, 3=UTF-16be).</summary>
    public required SharcTextEncoding TextEncoding { get; init; }

    /// <summary>Schema format number (1–4).</summary>
    public required int SchemaFormat { get; init; }

    /// <summary>User version integer (settable by PRAGMA user_version).</summary>
    public required int UserVersion { get; init; }

    /// <summary>Application ID (settable by PRAGMA application_id).</summary>
    public required int ApplicationId { get; init; }

    /// <summary>SQLite version that last modified the database.</summary>
    public required int SqliteVersion { get; init; }

    /// <summary>Whether the database uses WAL journal mode.</summary>
    public required bool IsWalMode { get; init; }

    /// <summary>Whether this database is Sharc-encrypted.</summary>
    public required bool IsEncrypted { get; init; }

    /// <summary>Total database size in bytes.</summary>
    public long DatabaseSize => (long)PageSize * PageCount;
}

/// <summary>
/// SQLite text encoding values.
/// </summary>
public enum SharcTextEncoding
{
    /// <summary>UTF-8 encoding (most common).</summary>
    Utf8 = 1,

    /// <summary>UTF-16 little-endian.</summary>
    Utf16Le = 2,

    /// <summary>UTF-16 big-endian.</summary>
    Utf16Be = 3
}