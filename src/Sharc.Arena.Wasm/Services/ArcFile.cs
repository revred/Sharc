// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Represents a single .arc file — a SharcDatabase with its own ledger and agent registry.
/// Each arc file maintains an independent hash-chain and can be persisted to OPFS.
/// </summary>
public sealed class ArcFile : IDisposable
{
    /// <summary>Arc file name (e.g., "telemetry.arc").</summary>
    public string Name { get; }

    /// <summary>The underlying Sharc database instance.</summary>
    public SharcDatabase Database { get; }

    /// <summary>This arc's append-only trust ledger.</summary>
    public LedgerManager Ledger { get; }

    /// <summary>This arc's agent registry.</summary>
    public AgentRegistry Registry { get; }

    /// <summary>Whether this arc should be persisted to OPFS.</summary>
    public bool IsDurable { get; }

    /// <summary>Whether this arc was restored from OPFS on initialization.</summary>
    public bool IsOpfsBacked { get; private set; }

    /// <summary>Number of ledger entries appended in this session.</summary>
    public int LedgerEntryCount { get; private set; }

    private ArcFile(string name, SharcDatabase db, bool durable)
    {
        Name = name;
        Database = db;
        Ledger = new LedgerManager(db);
        Registry = new AgentRegistry(db);
        IsDurable = durable;
    }

    /// <summary>
    /// Creates a fresh in-memory arc file with empty trust tables.
    /// </summary>
    public static ArcFile CreateInMemory(string name, bool durable = false)
    {
        var db = SharcDatabase.CreateInMemory();
        return new ArcFile(name, db, durable);
    }

    /// <summary>
    /// Restores an arc file from a byte array (e.g., loaded from OPFS).
    /// </summary>
    public static ArcFile FromBytes(string name, byte[] data, bool durable = true)
    {
        var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var arc = new ArcFile(name, db, durable) { IsOpfsBacked = true };
        return arc;
    }

    /// <summary>
    /// Appends a payload to this arc's ledger.
    /// </summary>
    public async Task AppendAsync(TrustPayload payload, ISharcSigner signer)
    {
        await Ledger.AppendAsync(payload, signer);
        LedgerEntryCount++;
    }

    /// <summary>
    /// Registers an agent in this arc's registry.
    /// </summary>
    public void RegisterAgent(AgentInfo agent)
    {
        Registry.RegisterAgent(agent);
    }

    /// <summary>
    /// Exports the database as a byte array for OPFS persistence.
    /// Reads all pages through the page source interface.
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

    /// <summary>
    /// Verifies the cryptographic integrity of this arc's ledger chain.
    /// </summary>
    public bool VerifyIntegrity() => Ledger.VerifyIntegrity();

    /// <inheritdoc />
    public void Dispose() => Database.Dispose();
}
