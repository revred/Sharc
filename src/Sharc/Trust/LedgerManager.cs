using System.Buffers.Binary;
using System.Security.Cryptography;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Crypto;
using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Manages the distributed ledger for agent trust and provenance.
/// </summary>
public sealed class LedgerManager
{
    private const string LedgerTableName = "_sharc_ledger";
    private readonly SharcDatabase _db;
    private readonly AgentRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="LedgerManager"/> class.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public LedgerManager(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _registry = new AgentRegistry(db);
    }

    /// <summary>
    /// Appends a new context payload to the ledger.
    /// </summary>
    /// <param name="contextPayload">The payload to append.</param>
    /// <param name="signer">The signer to use for attribution.</param>
    /// <param name="transaction">Optional active transaction. If null, a new one is created and committed.</param>
    public void Append(string contextPayload, ISharcSigner signer, Transaction? transaction = null)
    {
        var table = _db.Schema.GetTable(LedgerTableName);
        
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        try
        {
            // Use the transaction's shadow source for writes
            var source = tx.GetShadowSource();
            var mutator = new BTreeMutator(source, _db.Header.UsablePageSize);
            
            // Get last entry to link the hash chain
            // Note: We scan the tree via the *current* transaction view to see uncommitted changes if any
            // But GetLastEntry uses _db.PageSource which is redirected to tx by BeginTransaction/ProxySource.
            // So simply calling GetLastEntry is correct.
            var lastEntry = GetLastEntry(table.RootPage);
    
            long nextSequence = (lastEntry?.SequenceNumber ?? 0) + 1;
            byte[] prevHash = lastEntry?.PayloadHash ?? new byte[32];
            byte[] payloadData = System.Text.Encoding.UTF8.GetBytes(contextPayload);
            byte[] payloadHash = SharcHash.Compute(payloadData);
    
            // F6: Use Microseconds for higher precision
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000; 
    
            byte[] dataToSign = new byte[prevHash.Length + payloadHash.Length + 8];
            prevHash.CopyTo(dataToSign, 0);
            payloadHash.CopyTo(dataToSign, prevHash.Length);
            BinaryPrimitives.WriteInt64BigEndian(dataToSign.AsSpan(prevHash.Length + payloadHash.Length), nextSequence);
            
            byte[] signature = signer.Sign(dataToSign);
    
            var entry = new LedgerEntry(
                nextSequence,
                timestamp,
                signer.AgentId,
                payloadHash,
                prevHash,
                signature);
    
            var columns = new[]
            {
                ColumnValue.FromInt64(1, entry.SequenceNumber),
                ColumnValue.FromInt64(2, entry.Timestamp),
                ColumnValue.Text(entry.SequenceNumber, System.Text.Encoding.UTF8.GetBytes(entry.AgentId)),
                ColumnValue.Blob(entry.SequenceNumber, entry.PayloadHash),
                ColumnValue.Blob(entry.SequenceNumber, entry.PreviousHash),
                ColumnValue.Blob(entry.SequenceNumber, entry.Signature)
            };
    
            int recordSize = RecordEncoder.ComputeEncodedSize(columns);
            byte[] recordBuffer = new byte[recordSize];
            RecordEncoder.EncodeRecord(columns, recordBuffer);
    
            // F1: Use BTreeMutator to handle page splits
            // F4: Removed custom page split logic duplication
            mutator.Insert((uint)table.RootPage, entry.SequenceNumber, recordBuffer);
            
            if (ownsTransaction) tx.Commit();
        }
        finally
        {
            if (ownsTransaction) tx.Dispose();
        }
    }

    /// <summary>
    /// Verifies the cryptographic integrity of the ledger chain.
    /// </summary>
    /// <param name="activeSigners">Optional dictionary of known agent public keys for signature verification.</param>
    /// <returns>True if the chain is valid; false otherwise.</returns>
    public bool VerifyIntegrity(IReadOnlyDictionary<string, byte[]>? activeSigners = null)
    {
        using var cursor = _db.CreateReader(LedgerTableName);
        
        byte[] expectedPrevHash = new byte[32];
        long expectedSequence = 1;

        while (cursor.Read())
        {
            long seq = cursor.GetInt64(0);
            long ts = cursor.GetInt64(1);
            string agentId = cursor.GetString(2);
            byte[] payloadHash = cursor.GetBlob(3).ToArray();
            byte[] prevHash = cursor.GetBlob(4).ToArray();
            byte[] signature = cursor.GetBlob(5).ToArray();

            // 1. Check sequence
            if (seq != expectedSequence) return false;

            // 2. Check previous hash link
            if (!prevHash.AsSpan().SequenceEqual(expectedPrevHash)) return false;

            // 3. Verify signature (Who signed What and When)
            byte[] dataToVerify = new byte[prevHash.Length + payloadHash.Length + 8];
            prevHash.CopyTo(dataToVerify, 0);
            payloadHash.CopyTo(dataToVerify, prevHash.Length);
            BinaryPrimitives.WriteInt64BigEndian(dataToVerify.AsSpan(prevHash.Length + payloadHash.Length), seq);

            if (activeSigners != null && activeSigners.TryGetValue(agentId, out var publicKey))
            {
                if (!SharcSigner.Verify(dataToVerify, signature, publicKey))
                    return false;
            }
            else
            {
                // Fallback to AgentRegistry
                var agentRecord = _registry.GetAgent(agentId);
                if (agentRecord == null)
                    return false; // Unknown agent — cannot verify signature

                if (!SharcSigner.Verify(dataToVerify, signature, agentRecord.PublicKey))
                    return false;
            }
            
            expectedPrevHash = payloadHash;
            expectedSequence++;
        }

        return true;
    }

    /// <summary>
    /// Exports ledger entries from a specific sequence number.
    /// </summary>
    /// <param name="fromSequence">The sequence number to start from (inclusive).</param>
    /// <returns>A list of raw record bytes.</returns>
    public List<byte[]> ExportDeltas(long fromSequence)
    {
        var deltas = new List<byte[]>();
        using var reader = _db.CreateReader(LedgerTableName);
        
        while (reader.Read())
        {
            if (reader.GetInt64(0) >= fromSequence)
            {
                var columns = new[]
                {
                    ColumnValue.FromInt64(1, reader.GetInt64(0)),
                    ColumnValue.FromInt64(2, reader.GetInt64(1)),
                    ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes(reader.GetString(2))),
                    ColumnValue.Blob(1, reader.GetBlob(3).ToArray()),
                    ColumnValue.Blob(1, reader.GetBlob(4).ToArray()),
                    ColumnValue.Blob(1, reader.GetBlob(5).ToArray())
                };
                
                int size = RecordEncoder.ComputeEncodedSize(columns);
                byte[] buffer = new byte[size];
                RecordEncoder.EncodeRecord(columns, buffer);
                deltas.Add(buffer);
            }
        }
        return deltas;
    }

    /// <summary>
    /// Imports ledger deltas into the local ledger.
    /// </summary>
    /// <param name="deltas">The raw record bytes to import.</param>
    /// <param name="transaction">Optional active transaction.</param>
    public void ImportDeltas(IEnumerable<byte[]> deltas, Transaction? transaction = null)
    {
        var table = _db.Schema.GetTable(LedgerTableName);
        
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        try
        {
            var source = tx.GetShadowSource();
            var mutator = new BTreeMutator(source, _db.Header.UsablePageSize);

            foreach (var delta in deltas)
            {
                var decoded = _db.RecordDecoder.DecodeRecord(delta);
                long seq = decoded[0].AsInt64();
                byte[] payloadHash = decoded[3].AsBytes().ToArray();
                byte[] prevHash = decoded[4].AsBytes().ToArray();
                byte[] signature = decoded[5].AsBytes().ToArray();
                string agentId = decoded[2].AsString();
    
                var lastEntry = GetLastEntry(table.RootPage);
                if (lastEntry != null)
                {
                    if (seq != lastEntry.SequenceNumber + 1)
                        throw new InvalidOperationException($"Out of sequence delta: expected {lastEntry.SequenceNumber + 1}, got {seq}");
                    if (!prevHash.AsSpan().SequenceEqual(lastEntry.PayloadHash))
                        throw new InvalidOperationException($"Hash chain break in delta at sequence {seq}");
                }
                else if (seq != 1)
                {
                    throw new InvalidOperationException("First imported delta must have sequence 1");
                }
    
                byte[] dataToVerify = new byte[prevHash.Length + payloadHash.Length + 8];
                prevHash.CopyTo(dataToVerify, 0);
                payloadHash.CopyTo(dataToVerify, prevHash.Length);
                BinaryPrimitives.WriteInt64BigEndian(dataToVerify.AsSpan(prevHash.Length + payloadHash.Length), seq);
    
                var agent = _registry.GetAgent(agentId);
                if (agent == null)
                {
                     throw new InvalidOperationException($"Unknown agent in delta: {agentId}");
                }
    
                if (!SharcSigner.Verify(dataToVerify, signature, agent.PublicKey))
                {
                    throw new InvalidOperationException($"Invalid signature in delta at sequence {seq}");
                }
    
                // F1: Use BTreeMutator
                mutator.Insert((uint)table.RootPage, seq, delta);
            }
            
            if (ownsTransaction) tx.Commit();
        }
        finally
        {
            if (ownsTransaction) tx.Dispose();
        }
    }

    private LedgerEntry? GetLastEntry(int rootPage)
    {
        uint pageNum = (uint)rootPage;
        
        while (true)
        {
            // Use PageSource from DB (proxied to active transaction if any)
            var page = _db.PageSource.GetPage(pageNum);
            int headerOffset = (pageNum == 1) ? 100 : 0;
            var header = BTreePageHeader.Parse(page.Slice(headerOffset));
            
            if (header.IsLeaf)
            {
                if (header.CellCount == 0) return null;
    
                int cellPtrOffset = headerOffset + header.HeaderSize + (header.CellCount - 1) * 2;
                ushort cellOffset = BinaryPrimitives.ReadUInt16BigEndian(page.Slice(cellPtrOffset));
                
                int cellHeaderSize = CellParser.ParseTableLeafCell(page.Slice(cellOffset), out int payloadSize, out long rowId);
                
                var payload = page.Slice(cellOffset + cellHeaderSize, payloadSize);
                var decoded = _db.RecordDecoder.DecodeRecord(payload);
    
                return new LedgerEntry(
                    decoded[0].AsInt64(),
                    decoded[1].AsInt64(),
                    decoded[2].AsString(),
                    decoded[3].AsBytes().ToArray(),
                    decoded[4].AsBytes().ToArray(),
                    decoded[5].AsBytes().ToArray()
                );
            }
            else
            {
                // Interior node: follow the right-most child
                // The right-most child is stored in the header's RightChildPage field
                pageNum = header.RightChildPage;
            }
        }
    }
}
