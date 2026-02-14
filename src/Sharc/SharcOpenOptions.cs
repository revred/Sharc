// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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

    /// <summary>
    /// If true, the database is opened for writing (if supported by the page source).
    /// </summary>
    public bool Writable { get; set; }
}