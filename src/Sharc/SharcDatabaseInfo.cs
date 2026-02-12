/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc;

/// <summary>
/// Read-only metadata from the SQLite database header.
/// </summary>
public sealed class SharcDatabaseInfo
{
    /// <summary>Page size in bytes (512â€“65536).</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of pages in the database.</summary>
    public required int PageCount { get; init; }

    /// <summary>Text encoding (1=UTF-8, 2=UTF-16le, 3=UTF-16be).</summary>
    public required SharcTextEncoding TextEncoding { get; init; }

    /// <summary>Schema format number (1â€“4).</summary>
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
