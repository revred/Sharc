// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Sharc.Arc.Diff;
using Sharc.Trust;

namespace Sharc.Archive.Sync;

/// <summary>
/// Orchestrates fragment synchronization between archive files using the
/// F-3 cross-arc infrastructure (ArcDiffer, LedgerManager delta export/import).
/// </summary>
public static class FragmentSyncProtocol
{
    /// <summary>
    /// Compare the ledger state of two archives to determine sync feasibility.
    /// </summary>
    public static SyncPreflightResult Preflight(ArcHandle local, ArcHandle remote)
    {
        var diff = ArcDiffer.DiffLedger(local, remote);

        return new SyncPreflightResult(
            InSync: diff.IsIdentical,
            HasConflict: diff.DivergenceSequence.HasValue
                         && diff.LeftOnlyCount > 0
                         && diff.RightOnlyCount > 0,
            DivergenceSequence: diff.DivergenceSequence,
            LocalOnlyCount: diff.LeftOnlyCount,
            RemoteOnlyCount: diff.RightOnlyCount,
            CommonPrefixLength: diff.CommonPrefixLength);
    }

    /// <summary>
    /// Export ledger deltas from an archive starting at a given sequence.
    /// Returns a framed binary payload via <see cref="DeltaSerializer"/>.
    /// </summary>
    public static SyncExportResult Export(SharcDatabase db, long fromSequence)
    {
        var ledger = new LedgerManager(db);
        var deltas = ledger.ExportDeltas(fromSequence);
        var payload = DeltaSerializer.Serialize(deltas);

        return new SyncExportResult(
            FromSequence: fromSequence,
            DeltaCount: deltas.Count,
            Payload: payload);
    }

    /// <summary>
    /// Import ledger deltas into a local archive from a framed binary payload.
    /// Validates chain continuity before committing.
    /// </summary>
    public static SyncImportResult Import(SharcDatabase db, byte[] payload)
    {
        try
        {
            var deltas = DeltaSerializer.Deserialize(payload);
            if (deltas.Count == 0)
                return new SyncImportResult(0, 0, true, null);

            var ledger = new LedgerManager(db);
            ledger.ImportDeltas(deltas);

            // Determine new ledger sequence by counting entries
            long newSequence = CountLedgerEntries(db);

            return new SyncImportResult(
                ImportedCount: deltas.Count,
                NewLedgerSequence: newSequence,
                Success: true,
                ErrorMessage: null);
        }
        catch (InvalidOperationException ex)
        {
            return new SyncImportResult(0, 0, false, ex.Message);
        }
    }

    /// <summary>
    /// Full sync: preflight check, then import remote deltas if safe.
    /// Returns error if ledgers have diverged (conflict).
    /// </summary>
    public static SyncImportResult SyncFromRemote(
        ArcHandle local, ArcHandle remote, ManifestWriter? manifestWriter = null)
    {
        var preflight = Preflight(local, remote);

        if (preflight.InSync)
            return new SyncImportResult(0, 0, true, null);

        if (preflight.HasConflict)
            return new SyncImportResult(0, 0, false,
                $"Ledger conflict at sequence {preflight.DivergenceSequence}. " +
                $"Local has {preflight.LocalOnlyCount} unique entries, " +
                $"remote has {preflight.RemoteOnlyCount} unique entries.");

        // Safe to import: remote has entries we don't have, shared prefix is intact
        long fromSequence = preflight.CommonPrefixLength + 1;
        var exportResult = Export(remote.Database, fromSequence);

        if (exportResult.DeltaCount == 0)
            return new SyncImportResult(0, 0, true, null);

        var importResult = Import(local.Database, exportResult.Payload);

        // Update manifest if writer provided and import succeeded
        if (importResult.Success && manifestWriter != null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            manifestWriter.Insert(new ManifestRecord(
                FragmentId: remote.Name,
                Version: 1,
                SourceUri: remote.Uri?.ToString(),
                LastSyncAt: now,
                EntryCount: exportResult.DeltaCount,
                LedgerSequence: importResult.NewLedgerSequence,
                Checksum: null,
                Metadata: null));
        }

        return importResult;
    }

    private static long CountLedgerEntries(SharcDatabase db)
    {
        long count = 0;
        using var reader = db.CreateReader("_sharc_ledger");
        while (reader.Read()) count++;
        return count;
    }
}
