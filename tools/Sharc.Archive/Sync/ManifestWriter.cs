// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Codec;
using Sharc.Core;

namespace Sharc.Archive.Sync;

/// <summary>
/// CRUD operations for the <c>_sharc_manifest</c> tracking table.
/// Each row represents a known sync fragment with its last-known state.
/// </summary>
public sealed class ManifestWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;
    private bool _disposed;

    public ManifestWriter(SharcDatabase db)
    {
        _db = db;
        _writer = SharcWriter.From(db);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    /// <summary>Insert a new manifest entry. Returns the row id.</summary>
    public long Insert(ManifestRecord r)
    {
        return _writer.Insert("_sharc_manifest",
            TextVal(r.FragmentId),
            ColumnValue.FromInt64(1, r.Version),
            NullableTextVal(r.SourceUri),
            ColumnValue.FromInt64(1, r.LastSyncAt),
            ColumnValue.FromInt64(1, r.EntryCount),
            ColumnValue.FromInt64(1, r.LedgerSequence),
            NullableBlobVal(r.Checksum),
            MetadataVal(r.Metadata));
    }

    /// <summary>Read all manifest entries.</summary>
    public IReadOnlyList<ManifestRecord> ReadAll()
    {
        var results = new List<ManifestRecord>();
        using var reader = _db.CreateReader("_sharc_manifest");
        while (reader.Read())
        {
            results.Add(new ManifestRecord(
                reader.GetString(0),    // fragment_id
                (int)reader.GetInt64(1), // version
                reader.IsNull(2) ? null : reader.GetString(2), // source_uri
                reader.GetInt64(3),     // last_sync_at
                (int)reader.GetInt64(4), // entry_count
                reader.GetInt64(5),     // ledger_sequence
                reader.IsNull(6) ? null : reader.GetBlob(6),   // checksum
                reader.IsNull(7) ? null : SharcCbor.Decode(reader.GetBlob(7)))); // metadata
        }
        return results;
    }

    /// <summary>Find a manifest entry by fragment id. Returns null if not found.</summary>
    public ManifestRecord? FindByFragmentId(string fragmentId)
    {
        using var reader = _db.CreateReader("_sharc_manifest");
        while (reader.Read())
        {
            if (reader.GetString(0) == fragmentId)
            {
                return new ManifestRecord(
                    reader.GetString(0),
                    (int)reader.GetInt64(1),
                    reader.IsNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    (int)reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.IsNull(6) ? null : reader.GetBlob(6),
                    reader.IsNull(7) ? null : SharcCbor.Decode(reader.GetBlob(7)));
            }
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ColumnValue TextVal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    private static ColumnValue NullableTextVal(string? value)
    {
        if (value == null) return ColumnValue.Null();
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    private static ColumnValue NullableBlobVal(byte[]? value)
    {
        if (value == null) return ColumnValue.Null();
        return ColumnValue.Blob(2 * value.Length + 12, value);
    }

    private static ColumnValue MetadataVal(IDictionary<string, object?>? metadata)
    {
        if (metadata == null) return ColumnValue.Null();
        var bytes = SharcCbor.Encode(metadata);
        return ColumnValue.Blob(2 * bytes.Length + 12, bytes);
    }
}
