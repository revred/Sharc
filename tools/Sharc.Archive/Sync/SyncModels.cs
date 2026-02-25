// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Archive.Sync;

/// <summary>A row from the <c>_sharc_manifest</c> tracking table.</summary>
public sealed record ManifestRecord(
    string FragmentId,
    int Version,
    string? SourceUri,
    long LastSyncAt,
    int EntryCount,
    long LedgerSequence,
    byte[]? Checksum,
    IDictionary<string, object?>? Metadata);

/// <summary>Result of exporting deltas from a local archive.</summary>
public sealed record SyncExportResult(
    long FromSequence,
    int DeltaCount,
    byte[] Payload);

/// <summary>Result of importing deltas into a local archive.</summary>
public sealed record SyncImportResult(
    int ImportedCount,
    long NewLedgerSequence,
    bool Success,
    string? ErrorMessage);

/// <summary>Pre-flight sync check: compares local vs remote ledger state.</summary>
public sealed record SyncPreflightResult(
    bool InSync,
    bool HasConflict,
    long? DivergenceSequence,
    int LocalOnlyCount,
    int RemoteOnlyCount,
    int CommonPrefixLength);
