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
/// Configuration options for opening a Sharc database.
/// </summary>
public sealed class SharcOpenOptions
{
    /// <summary>
    /// Encryption settings. Null for unencrypted databases.
    /// </summary>
    public SharcEncryptionOptions? Encryption { get; set; }

    /// <summary>
    /// Maximum number of pages to cache in memory. Default is 2000.
    /// Set to 0 to disable caching (useful for memory-backed databases).
    /// </summary>
    public int PageCacheSize { get; set; } = 2000;

    /// <summary>
    /// If true, the database file is read entirely into memory on open.
    /// Provides faster access at the cost of memory usage.
    /// </summary>
    public bool PreloadToMemory { get; set; }

    /// <summary>
    /// File share mode when opening file-backed databases.
    /// Default is <see cref="FileShare.ReadWrite"/> to coexist with SQLite writers.
    /// </summary>
    public FileShare FileShareMode { get; set; } = FileShare.ReadWrite;
}
