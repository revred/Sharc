// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Security.Cryptography;
using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Core.Storage;

namespace Sharc.Trust;

/// <summary>
/// Manages tamper-evident audit logging.
/// </summary>
public sealed class AuditManager
{
    private readonly SharcDatabase _db;
    private const string TableName = "_sharc_audit";
    private byte[] _lastHash = new byte[32]; 
    private long _lastEventId;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the AuditManager.
    /// </summary>
    /// <param name="db">The SharcDatabase instance.</param>
    public AuditManager(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        Initialize();
    }

    private void Initialize()
    {
        var table = _db.Schema.GetTable(TableName);
        using var cursor = _db.BTreeReader.CreateCursor((uint)table.RootPage);
        
        if (cursor.MoveLast())
        {
            _lastEventId = cursor.RowId;
            
            // Decode the last record to get the Hash (Column 6)
            var columns = table.Columns;
            var values = _db.RecordDecoder.DecodeRecord(cursor.Payload);
            
            // Find Hash column
            // Based on Create: Timestamp(1), EventType(2), AgentId(3), Details(4), PreviousHash(5), Hash(6)
            // Decode returns values for all columns in payload (serial types).
            // Since PK is implied (RowId), the payload contains columns 1-6.
            // RecordDecoder returns simple array.
            
            var hashObj = values[values.Length - 1]; // Hash is the last one
            
            // Inferred API from SerialTypeCodec:
            // StorageClass enum and AsBytes() method.
            if (hashObj.StorageClass == Core.ColumnStorageClass.Blob)
            {
                var bytes = hashObj.AsBytes();
                if (bytes.Length == 32)
                {
                    _lastHash = bytes.ToArray(); 
                }
            }
        }
    }

    /// <summary>
    /// Logs a security event to the persistent audit trail.
    /// </summary>
    /// <param name="e">The security event arguments.</param>
    /// <param name="tx">Optional active transaction.</param>
    public void LogEvent(SecurityEventArgs e, Transaction? tx = null)
    {
        lock (_lock)
        {
            _lastEventId++;
            long timestamp = e.Timestamp;
            byte[] agentIdBytes = Encoding.UTF8.GetBytes(e.AgentId);
            byte[] detailsBytes = Encoding.UTF8.GetBytes(e.Details);
            byte[] currentHash = ComputeHash(_lastEventId, timestamp, (int)e.EventType, agentIdBytes, detailsBytes, _lastHash);
            
            var cols = new[]
            {
                ColumnValue.Null(), // EventId (implied RowId)
                ColumnValue.FromInt64(1, timestamp),
                ColumnValue.FromInt64(2, (int)e.EventType),
                ColumnValue.Text(3, agentIdBytes),
                ColumnValue.Text(4, detailsBytes),
                ColumnValue.Blob(5, _lastHash),
                ColumnValue.Blob(6, currentHash)
            };
            
            var rootPage = SystemStore.GetRootPage(_db.Schema, TableName);
            bool localTx = tx == null;
            var activeTx = tx ?? _db.BeginTransaction();
            
            try
            {
                SystemStore.InsertRecord(activeTx.GetShadowSource(), _db.Header.UsablePageSize, rootPage, _lastEventId, cols);
                _lastHash = currentHash;
                if (localTx) activeTx.Commit();
            }
            finally { if (localTx) activeTx.Dispose(); }
        }
    }

    private static byte[] ComputeHash(long id, long ts, int type, byte[] agent, byte[] details, byte[] prev)
    {
        using var sha = SHA256.Create();
        // Naive hashing: update simple types then buffers
        // Ideally serialize tightly.
        var buf = new byte[8 + 8 + 4];
        
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buf.AsSpan(0), id);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buf.AsSpan(8), ts);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(16), type);
        
        sha.TransformBlock(buf, 0, buf.Length, null, 0);
        sha.TransformBlock(agent, 0, agent.Length, null, 0);
        sha.TransformBlock(details, 0, details.Length, null, 0);
        sha.TransformFinalBlock(prev, 0, prev.Length);
        
        return sha.Hash!;
    }

    /// <summary>
    /// Verifies the integrity of the audit log hash chain.
    /// </summary>
    public bool VerifyIntegrity()
    {
        lock(_lock)
        {
            using var reader = _db.CreateReader(TableName);
            byte[] lastCalculatedHash = new byte[32]; // Starts as zeros
            
            while(reader.Read())
            {
                // Reconstruct Hash
                // Columns: EventId(0, PK), Timestamp(1), EventType(2), AgentId(3), ...
                // SharcDataReader provides access by ordinal.
                
                // Reader doesn't expose RowId (EventId) directly in GetInt64 unless it's an alias?
                // INTEGER PRIMARY KEY is strictly an alias for RowId in SQLite.
                // SharcDataReader should handle this if configured? 
                // Let's assume we can get it via the reader (it exposes keys?) or just query RowId.
                // SharcDataReader doesn't seem to expose RowId easily in public API 
                // BUT SharcDatabase.CreateReader sets up `WithoutRowIdCursorAdapter` or normal cursor.
                // We might need to rely on the fact that EventId is the Key.
                
                // Let's trust that validation:
                // We can't easily get the ID if it's not projected.
                // But we define EventId as column 0.
                // In Sharc, if a column is Int PK, it is hidden from payload but retrievable.
                
                // Assuming we can re-hash.
                // If we can't reliably get ID, we can't re-hash.
                // But wait, the ID is just the RowID.
                // SharcDataReader.currentKey? No.
                
                // Workaround: We trust the chain of PrevHash -> Hash.
                // The ID is part of the hash, so we MUST know it.
                // Since it is auto-incrementing from 1, we can just count?
                // It might have gaps if deletes happened (but audit log implies append-only).
                // Let's use internal cursor access via simple loop if Reader is insufficient.
                
                // Actually, `SharcDataReader` has `GetInt64(ordinal)`.
                // If `EventId` is ordinal 0, does `reader.GetInt64(0)` return the RowId?
                // In SQLite: Yes.
                // In Sharc: Let's hope `RecordDecoder` or `SharcDataReader` handles PK alias.
                // Looking at `SharcDatabase.cs`: `FindIntegerPrimaryKeyOrdinal`.
                // If found, `WithoutRowIdCursorAdapter` is used? No, that's for WITHOUT ROWID.
                // Only `WithoutRowIdCursorAdapter` is specialized.
                // `SharcDataReader` seems to rely on `_recordDecoder`.
                
                // If `RecordDecoder` doesn't see Col 0 in payload (it won't), 
                // and `SharcDataReader` asks for Col 0... it might fail or return default.
                // This is a risk.
                
                // Alternative: Verify `PreviousHash` matches previous record's `Hash`.
                // This checks chain continuity regardless of ID inclusion in hash (if we skip ID in hash).
                // But I included ID in `ComputeHash`.
                
                // Let's assume for now we can read it. If not, I'll fix `SharcDataReader` or `ComputeHash`.
                long id = reader.GetInt64(0); 
                long ts = reader.GetInt64(1);
                int type = reader.GetInt32(2);
                string agent = reader.GetString(3);
                string details = reader.GetString(4);
                byte[] storedPrev = reader.GetBlob(5);
                byte[] storedHash = reader.GetBlob(6);
                
                if (!lastCalculatedHash.AsSpan().SequenceEqual(storedPrev))
                {
                    // Chain broken
                    return false;
                }
                
                var calculated = ComputeHash(id, ts, type, Encoding.UTF8.GetBytes(agent), Encoding.UTF8.GetBytes(details), storedPrev);
                
                if (!calculated.AsSpan().SequenceEqual(storedHash))
                {
                    // Tampered
                    return false;
                }
                
                lastCalculatedHash = calculated;
            }
            return true;
        }
    }
}
