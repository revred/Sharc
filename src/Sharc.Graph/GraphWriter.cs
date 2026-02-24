// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Text;
using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Trust;

namespace Sharc.Graph;

/// <summary>
/// Provides typed write operations for graph nodes (concepts) and edges (relations).
/// Delegates to <see cref="SharcWriter"/> for actual INSERT/DELETE operations.
/// Optionally records provenance entries in the trust ledger on every mutation.
/// Optionally archives deleted edges to a history table for audit trails.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership:</b> The caller retains ownership of <see cref="SharcWriter"/>,
/// <see cref="LedgerManager"/>, and <see cref="ISharcSigner"/>. This class does
/// not dispose any of its dependencies.
/// </para>
/// <para>
/// <b>Thread safety:</b> Not thread-safe. All access must be serialized by the caller.
/// </para>
/// <para>
/// <b>Zero-cost ledger:</b> When <c>ledger</c> or <c>signer</c> is null,
/// no provenance overhead is incurred — a single null check per operation.
/// </para>
/// </remarks>
public sealed class GraphWriter : IGraphWriter
{
    // ── Dependencies (not owned) ──
    private readonly SharcWriter _writer;
    private readonly ISchemaAdapter _schema;
    private readonly LedgerManager? _ledger;
    private readonly ISharcSigner? _signer;
    private readonly IChangeNotifier? _notifier;

    // ── Derived state (computed once at construction) ──
    private readonly bool _historyEnabled;
    private readonly bool _ledgerEnabled;

    // ── Lifecycle ──
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="GraphWriter"/> wrapping the given <see cref="SharcWriter"/>.
    /// </summary>
    /// <param name="writer">The underlying write engine. Must not be null.</param>
    /// <param name="schema">Schema adapter for table/column mapping. Defaults to <see cref="NativeSchemaAdapter"/>.</param>
    /// <param name="ledger">Optional ledger manager for provenance recording. When null, no ledger entries are written.</param>
    /// <param name="signer">Optional cryptographic signer for ledger entries. Required when <paramref name="ledger"/> is provided.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="writer"/> is null.</exception>
    /// <param name="notifier">Optional change notifier for publishing mutation events (F-4). When null, no events are published.</param>
    /// <exception cref="ArgumentException">When <paramref name="ledger"/> is provided without <paramref name="signer"/>.</exception>
    public GraphWriter(SharcWriter writer, ISchemaAdapter? schema = null,
        LedgerManager? ledger = null, ISharcSigner? signer = null,
        IChangeNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (ledger != null && signer == null)
            throw new ArgumentException(
                "A signer is required when a ledger is provided.", nameof(signer));

        _writer = writer;
        _schema = schema ?? new NativeSchemaAdapter();
        _ledger = ledger;
        _signer = signer;
        _notifier = notifier;

        _historyEnabled = _schema.EdgeHistoryTableName != null
            && _writer.Database.Schema.TryGetTable(_schema.EdgeHistoryTableName) != null;
        _ledgerEnabled = _ledger != null && _signer != null;
    }

    /// <inheritdoc/>
    public NodeKey Intern(string id, NodeKey key, ConceptKind kind,
        string jsonData = "{}", string? nodeAlias = null, int? tokens = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(jsonData);

        // Idempotent: if a node with this key exists, return it
        if (TryFindNodeRowId(key, out _))
            return key;

        // Build column values in table column order:
        // id, key, kind, data, cvn, lvn, sync_status, updated_at, tokens, alias
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(jsonData);

        var values = new ColumnValue[10];
        values[0] = ColumnValue.Text(13 + 2 * idBytes.Length, idBytes);          // id
        values[1] = ColumnValue.FromInt64(6, key.Value);                         // key
        values[2] = ColumnValue.FromInt64(4, (int)kind);                         // kind
        values[3] = ColumnValue.Text(13 + 2 * dataBytes.Length, dataBytes);      // data
        values[4] = ColumnValue.FromInt64(1, 0);                                 // cvn
        values[5] = ColumnValue.FromInt64(1, 0);                                 // lvn
        values[6] = ColumnValue.FromInt64(1, 0);                                 // sync_status
        values[7] = ColumnValue.FromInt64(6, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // updated_at
        values[8] = tokens.HasValue
            ? ColumnValue.FromInt64(4, tokens.Value)
            : ColumnValue.Null();                                                 // tokens
        values[9] = nodeAlias != null
            ? ColumnValue.Text(13 + 2 * Encoding.UTF8.GetByteCount(nodeAlias), Encoding.UTF8.GetBytes(nodeAlias))
            : ColumnValue.Null();                                                 // alias

        _writer.Insert(_schema.NodeTableName, values);

        AppendProvenance("node:intern", $"key={key.Value},kind={kind},id={id}");
        PublishChange(kind, ChangeType.Create, new RecordId(_schema.NodeTableName, id, key), null);

        return key;
    }

    /// <inheritdoc/>
    public long Link(string id, NodeKey origin, NodeKey target, RelationKind kind,
        string jsonData = "{}", float weight = 1.0f)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(jsonData);

        // Build column values in table column order:
        // id, source_key, target_key, kind, data, cvn, lvn, sync_status, weight
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(jsonData);

        var values = new ColumnValue[9];
        values[0] = ColumnValue.Text(13 + 2 * idBytes.Length, idBytes);          // id
        values[1] = ColumnValue.FromInt64(6, origin.Value);                      // source_key
        values[2] = ColumnValue.FromInt64(6, target.Value);                      // target_key
        values[3] = ColumnValue.FromInt64(4, (int)kind);                         // kind
        values[4] = ColumnValue.Text(13 + 2 * dataBytes.Length, dataBytes);      // data
        values[5] = ColumnValue.FromInt64(1, 0);                                 // cvn
        values[6] = ColumnValue.FromInt64(1, 0);                                 // lvn
        values[7] = ColumnValue.FromInt64(1, 0);                                 // sync_status
        values[8] = ColumnValue.FromDouble(weight);                              // weight

        long rowId = _writer.Insert(_schema.EdgeTableName, values);

        AppendProvenance("edge:link",
            $"id={id},source={origin.Value},target={target.Value},kind={kind}");

        return rowId;
    }

    /// <inheritdoc/>
    public bool Remove(NodeKey key)
    {
        ThrowIfDisposed();

        if (!TryFindNodeRowId(key, out long nodeRowId))
            return false;

        // Delete connected edges first (both outgoing and incoming)
        RemoveEdgesForNode(key);

        bool deleted = _writer.Delete(_schema.NodeTableName, nodeRowId);

        if (deleted)
        {
            AppendProvenance("node:remove", $"key={key.Value}");
            // Use ConceptKind 0 for remove (kind unknown without re-reading)
            PublishChange(0, ChangeType.Delete, new RecordId(_schema.NodeTableName, null, key), null);
        }

        return deleted;
    }

    /// <inheritdoc/>
    public bool Unlink(long edgeRowId)
    {
        ThrowIfDisposed();

        if (_historyEnabled)
            ArchiveEdge(edgeRowId, "delete");

        bool deleted = _writer.Delete(_schema.EdgeTableName, edgeRowId);

        if (deleted)
            AppendProvenance("edge:unlink", $"rowId={edgeRowId}");

        return deleted;
    }

    /// <inheritdoc/>
    public void Commit()
    {
        // Auto-commit writer — each Insert/Delete is its own transaction.
        // This is a no-op, provided for interface completeness.
    }

    // ── Private: Node lookup ──

    /// <summary>
    /// Finds the rowid of a node with the given key by scanning the concepts table.
    /// </summary>
    private bool TryFindNodeRowId(NodeKey key, out long rowId)
    {
        rowId = 0;
        using var reader = _writer.Database.CreateReader(_schema.NodeTableName);
        while (reader.Read())
        {
            if (reader.GetInt64(1) == key.Value) // column 1 = key
            {
                rowId = reader.RowId;
                return true;
            }
        }
        return false;
    }

    // ── Private: Edge cleanup ──

    /// <summary>
    /// Removes all edges connected to the given node key (both outgoing and incoming).
    /// Archives each edge to history before deletion when history is enabled.
    /// </summary>
    private void RemoveEdgesForNode(NodeKey key)
    {
        var edgeRowIds = new List<long>();
        using (var reader = _writer.Database.CreateReader(_schema.EdgeTableName))
        {
            while (reader.Read())
            {
                long sourceKey = reader.GetInt64(1); // source_key
                long targetKey = reader.GetInt64(2); // target_key
                if (sourceKey == key.Value || targetKey == key.Value)
                    edgeRowIds.Add(reader.RowId);
            }
        }

        foreach (long edgeRowId in edgeRowIds)
        {
            if (_historyEnabled)
                ArchiveEdge(edgeRowId, "delete");
            _writer.Delete(_schema.EdgeTableName, edgeRowId);
        }
    }

    // ── Private: Edge history ──

    /// <summary>
    /// Archives an edge row to the history table before deletion.
    /// Reads the edge data, then inserts a copy with timestamp and operation type.
    /// </summary>
    private void ArchiveEdge(long edgeRowId, string operation)
    {
        string historyTable = _schema.EdgeHistoryTableName!;

        // Read the edge row before it's deleted
        string? id = null;
        long sourceKey = 0, targetKey = 0;
        int kind = 0;
        string data = "{}";
        double weight = 0.0;

        using (var reader = _writer.Database.CreateReader(_schema.EdgeTableName))
        {
            while (reader.Read())
            {
                if (reader.RowId == edgeRowId)
                {
                    id = reader.GetString(0);       // id
                    sourceKey = reader.GetInt64(1);  // source_key
                    targetKey = reader.GetInt64(2);  // target_key
                    kind = (int)reader.GetInt64(3);  // kind
                    data = reader.GetString(4);      // data
                    weight = reader.GetDouble(8);    // weight
                    break;
                }
            }
        }

        if (id == null) return; // Edge not found — nothing to archive

        // History table columns: id, source_key, target_key, kind, data, weight, archived_at, op
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var opBytes = Encoding.UTF8.GetBytes(operation);

        var values = new ColumnValue[8];
        values[0] = ColumnValue.Text(13 + 2 * idBytes.Length, idBytes);       // id
        values[1] = ColumnValue.FromInt64(6, sourceKey);                       // source_key
        values[2] = ColumnValue.FromInt64(6, targetKey);                       // target_key
        values[3] = ColumnValue.FromInt64(4, kind);                            // kind
        values[4] = ColumnValue.Text(13 + 2 * dataBytes.Length, dataBytes);   // data
        values[5] = ColumnValue.FromDouble(weight);                            // weight
        values[6] = ColumnValue.FromInt64(6, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // archived_at
        values[7] = ColumnValue.Text(13 + 2 * opBytes.Length, opBytes);       // op

        _writer.Insert(historyTable, values);
    }

    // ── Private: Ledger provenance ──

    /// <summary>
    /// Appends a provenance entry to the trust ledger when ledger is enabled.
    /// When ledger is null, this is a no-op (zero cost).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendProvenance(string action, string details)
    {
        if (!_ledgerEnabled) return;

        var payload = new TrustPayload(PayloadType.System, $"graph:{action}:{details}");
        _ledger!.Append(payload, _signer!);
    }

    // ── Private: Change notification (F-4) ──

    /// <summary>
    /// Publishes a change event to the notifier when one is configured.
    /// When notifier is null, this is a no-op (zero cost).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PublishChange(ConceptKind kind, ChangeType type, RecordId id, GraphRecord? snapshot)
    {
        if (_notifier == null) return;
        _notifier.Publish(kind, new ChangeEvent(type, id, null, type == ChangeType.Delete ? null : snapshot));
    }

    // ── Lifecycle ──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        // Does not dispose dependencies — caller retains ownership of
        // SharcWriter, LedgerManager, and ISharcSigner.
    }
}
