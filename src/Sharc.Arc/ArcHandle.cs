// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Trust;

namespace Sharc.Arc;

/// <summary>
/// Represents an opened .arc file — a <see cref="SharcDatabase"/> with its own
/// ledger and agent registry. Platform-agnostic: no OPFS or browser dependencies.
/// <para>
/// Use factory methods to create or open arcs:
/// <see cref="OpenLocal"/>, <see cref="FromMemory"/>, <see cref="CreateInMemory"/>.
/// </para>
/// </summary>
public sealed class ArcHandle : IDisposable
{
    /// <summary>Arc file name (e.g., "telemetry.arc").</summary>
    public string Name { get; }

    /// <summary>The URI this handle was resolved from (if available).</summary>
    public ArcUri? Uri { get; }

    /// <summary>The underlying Sharc database instance.</summary>
    public SharcDatabase Database { get; }

    /// <summary>This arc's append-only trust ledger.</summary>
    public LedgerManager Ledger { get; }

    /// <summary>This arc's agent registry.</summary>
    public AgentRegistry Registry { get; }

    internal ArcHandle(string name, SharcDatabase db, ArcUri? uri = null)
    {
        Name = name;
        Uri = uri;
        Database = db;
        Ledger = new LedgerManager(db);
        Registry = new AgentRegistry(db);
    }

    /// <summary>
    /// Opens a local arc file from disk for reading.
    /// </summary>
    public static ArcHandle OpenLocal(string path, SharcOpenOptions? options = null)
    {
        var db = SharcDatabase.Open(path, options);
        string name = Path.GetFileName(path);
        var uri = ArcUri.FromLocalPath(path);
        return new ArcHandle(name, db, uri);
    }

    /// <summary>
    /// Restores an arc file from a byte array (e.g., loaded from remote storage).
    /// </summary>
    public static ArcHandle FromMemory(string name, byte[] data, bool writable = false)
    {
        var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = writable });
        return new ArcHandle(name, db);
    }

    /// <summary>
    /// Creates a fresh in-memory arc file with empty trust tables.
    /// </summary>
    public static ArcHandle CreateInMemory(string name)
    {
        var db = SharcDatabase.CreateInMemory();
        return new ArcHandle(name, db);
    }

    /// <summary>
    /// Verifies the cryptographic integrity of this arc's ledger chain.
    /// </summary>
    public bool VerifyIntegrity() => Ledger.VerifyIntegrity();

    /// <summary>
    /// Exports the database as a byte array for transport or persistence.
    /// </summary>
    public byte[] ExportBytes()
    {
        var ps = Database.PageSource;
        int pageSize = Database.Header.PageSize;
        int pageCount = ps.PageCount;
        var bytes = new byte[pageSize * pageCount];

        for (int i = 1; i <= pageCount; i++)
            ps.ReadPage((uint)i, bytes.AsSpan((i - 1) * pageSize, pageSize));

        return bytes;
    }

    /// <inheritdoc />
    public void Dispose() => Database.Dispose();
}
