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
    /// Occurs when a security-relevant event happens (append success/failure, integrity check).
    /// </summary>
    public event EventHandler<SecurityEventArgs>? SecurityAudit;

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
        // Wrap string in TrustPayload (Text type, no economic value)
        var payload = new TrustPayload(PayloadType.Text, contextPayload);
        Append(payload, signer, transaction);
    }

    /// <summary>
    /// Appends a structured trust payload to the ledger.
    /// </summary>
    /// <param name="payload">The structured payload to append.</param>
    /// <param name="signer">The signer to use for attribution.</param>
    /// <param name="transaction">Optional active transaction. If null, a new one is created and committed.</param>
    public void Append(TrustPayload payload, ISharcSigner signer, Transaction? transaction = null)
    {
        var table = _db.Schema.GetTable(LedgerTableName);
        
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        // F2: Serialize Structured Payload
        byte[] payloadData = payload.ToBytes();
        
        try
        {
            var source = tx.GetShadowSource();
            var mutator = new BTreeMutator(source, _db.Header.UsablePageSize);
            
            // Get last entry to link the hash chain
            var lastEntry = GetLastEntry(table.RootPage);
    
            long nextSequence = (lastEntry?.SequenceNumber ?? 0) + 1;
            byte[] prevHash = lastEntry?.PayloadHash ?? new byte[32];
            byte[] payloadHash = SharcHash.Compute(payloadData);
    
            // F7: Validity Check
            // F6: Use Microseconds for higher precision
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000; 

            // Ensure the signer's key is valid at the time of signing.
            var agentInfo = _registry.GetAgent(signer.AgentId);
            
            // If we have agent info, check validity window AND AuthorityCeiling
            if (agentInfo != null)
            {
                long validStart = agentInfo.ValidityStart;
                long validEnd = agentInfo.ValidityEnd;
                
                // Ledger uses microseconds, AgentInfo/Tests use seconds (mostly).
                // Standardizing on seconds for the check.
                long tsSeconds = timestamp / 1000000;
                if (tsSeconds < validStart || (validEnd > 0 && tsSeconds > validEnd))
                {
                    var msg = $"Agent key is not valid at this time (Valid: {validStart}-{validEnd}, Current: {tsSeconds})";
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendRejected, signer.AgentId, msg));
                    throw new InvalidOperationException(msg);
                }

                // F7: Authority Ceiling Enforcement
                if (payload.EconomicValue.HasValue)
                {
                    // Convert decimal to ulong (assuming base currency units) or comparison logic.
                    // AgentInfo.AuthorityCeiling is ulong (e.g. cents or whole units).
                    // We treat EconomicValue as the amount to check.
                    // If EconomicValue is negative, it might be a refund, but usually authority limits absolute value.
                    // For now, we assume positive checks.
                    
                    if (payload.EconomicValue.Value > agentInfo.AuthorityCeiling)
                    {
                         var msg = $"Authority ceiling exceeded. Agent {signer.AgentId} limit: {agentInfo.AuthorityCeiling}, Requested: {payload.EconomicValue.Value}";
                         SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendRejected, signer.AgentId, msg));
                         throw new InvalidOperationException(msg);
                    }
                }

                // F7: Co-Signing Enforcement
                if (agentInfo.CoSignRequired)
                {
                    if (payload.CoSignatures == null || payload.CoSignatures.Count == 0)
                    {
                        var msg = $"Agent {signer.AgentId} requires co-signatures but none provided.";
                        SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendRejected, signer.AgentId, msg));
                        throw new InvalidOperationException(msg);
                    }

                    // Verify Co-Signatures
                    // Co-Signers sign the payload WITHOUT the CoSignatures field (to avoid circularity).
                    // We reconstruct the base payload to verify.
                    var basePayload = payload with { CoSignatures = null };
                    var baseData = basePayload.ToBytes();
                    var baseHash = SharcHash.Compute(baseData);

                    foreach (var sig in payload.CoSignatures)
                    {
                        var coSigner = _registry.GetAgent(sig.SignerId);
                        if (coSigner == null)
                            throw new InvalidOperationException($"Unknown co-signer: {sig.SignerId}");

                        // Check co-signer validity at time of THEIR signing
                        // Use microsecond precision for ledger compatibility if needed, but sig.Timestamp is just long.
                        // Assuming sig.Timestamp is Unix Milliseconds similar to payload generation time
                        long sigTsSeconds = sig.Timestamp / 1000;
                        if (sigTsSeconds < coSigner.ValidityStart || (coSigner.ValidityEnd > 0 && sigTsSeconds > coSigner.ValidityEnd))
                             throw new InvalidOperationException($"Co-signer {sig.SignerId} key expired or invalid at signing time.");

                        // Verify Signature
                        // They sign: BaseHash + Timestamp
                        byte[] signedData = new byte[baseHash.Length + 8];
                        baseHash.CopyTo(signedData, 0);
                        BinaryPrimitives.WriteInt64BigEndian(signedData.AsSpan(baseHash.Length), sig.Timestamp);

                        if (!SharcSigner.Verify(signedData, sig.Signature, coSigner.PublicKey))
                            throw new InvalidOperationException($"Invalid co-signature from {sig.SignerId}");
                            
                        // Prevent self-cosigning?
                        if (sig.SignerId == signer.AgentId)
                            throw new InvalidOperationException($"Agent {signer.AgentId} cannot co-sign their own payload.");
                    }
                }
            }


            byte[] dataToSign = new byte[prevHash.Length + payloadHash.Length + 8];
            prevHash.CopyTo(dataToSign, 0);
            payloadHash.CopyTo(dataToSign, prevHash.Length);
            BinaryPrimitives.WriteInt64BigEndian(dataToSign.AsSpan(prevHash.Length + payloadHash.Length), nextSequence);
            
            byte[] signature = signer.Sign(dataToSign);

            // F2: Store Payload
            // Payload is already serialized in 'payloadData'
            
            var entry = new LedgerEntry(
                nextSequence,
                timestamp,
                signer.AgentId,
                payloadData, 
                payloadHash,
                prevHash,
                signature);
    
            var columns = new[]
            {
                ColumnValue.FromInt64(1, entry.SequenceNumber),
                ColumnValue.FromInt64(2, entry.Timestamp),
                ColumnValue.Text(entry.SequenceNumber, System.Text.Encoding.UTF8.GetBytes(entry.AgentId)),
                ColumnValue.Blob(entry.SequenceNumber, entry.Payload),
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

            // Success Event
            SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendSuccess, signer.AgentId, $"seq={entry.SequenceNumber}"));
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
            // Index 3 is Payload, we don't need it for Hash Chain verification (Hash is in 4)
            // But we might want to verify PayloadHash matches Payload?
            // F2: Verify Payload Hash Integrity?
            byte[] payload = cursor.GetBlob(3).ToArray();
            byte[] payloadHash = cursor.GetBlob(4).ToArray();
            byte[] prevHash = cursor.GetBlob(5).ToArray();
            byte[] signature = cursor.GetBlob(6).ToArray();

            // 0. Verify Payload Hash
            byte[] computedHash = SharcHash.Compute(payload);
            if (!computedHash.AsSpan().SequenceEqual(payloadHash)) 
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Payload hash mismatch at seq {seq}"));
                return false;
            }

            // 0.1 Verify Co-Signatures (if structured payload)
            // We attempt to deserialize as TrustPayload. If it fails (e.g. raw blob), we skip.
            // But we know standard payloads are TrustPayload.
            // Note: This adds overhead but is required for deep audit.
            try 
            {
                var trustPayload = TrustPayload.FromBytes(payload);
                if (trustPayload != null && trustPayload.CoSignatures != null && trustPayload.CoSignatures.Count > 0)
                {
                    // Reconstruct base hash
                    var basePayload = trustPayload with { CoSignatures = null };
                    var baseHash = SharcHash.Compute(basePayload.ToBytes());

                    foreach (var sig in trustPayload.CoSignatures)
                    {
                       // We need co-signer public key.
                       // During integrity check, we might not have access to full history if keys rotated?
                       // We assume _registry has current keys. Ideally we need historical keys but AgentInfo has ValidityStart/End.
                       
                       var coSigner = _registry.GetAgent(sig.SignerId);
                       if (coSigner == null)
                       {
                           SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, sig.SignerId, $"Unknown co-signer at seq {seq}"));
                           return false;
                       }
                       
                       // Verify Signature: BaseHash + Timestamp
                       byte[] signedData = new byte[baseHash.Length + 8];
                       baseHash.CopyTo(signedData, 0);
                       BinaryPrimitives.WriteInt64BigEndian(signedData.AsSpan(baseHash.Length), sig.Timestamp);

                       if (!SharcSigner.Verify(signedData, sig.Signature, coSigner.PublicKey))
                       {
                           SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, sig.SignerId, $"Invalid co-signature at seq {seq}"));
                           return false;
                       }
                    }
                }
            }
            catch 
            {
                // Not a TrustPayload or deserialization failed. 
                // If it was supposed to be a TrustPayload, this is an issue, but for generic BLOB support allow pass.
                // However, Sharc.Trust treats everything as TrustPayload typically.
            }

            // 1. Check sequence
            if (seq != expectedSequence) 
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Sequence gap: {expectedSequence} -> {seq}"));
                return false;
            }

            // 2. Check previous hash link
            if (!prevHash.AsSpan().SequenceEqual(expectedPrevHash)) 
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Hash chain broken at seq {seq}"));
                return false;
            }

            // 3. Verify signature (Who signed What and When)
            byte[] dataToVerify = new byte[prevHash.Length + payloadHash.Length + 8];
            prevHash.CopyTo(dataToVerify, 0);
            payloadHash.CopyTo(dataToVerify, prevHash.Length);
            BinaryPrimitives.WriteInt64BigEndian(dataToVerify.AsSpan(prevHash.Length + payloadHash.Length), seq);

            if (activeSigners != null && activeSigners.TryGetValue(agentId, out var publicKey))
            {
                if (!SharcSigner.Verify(dataToVerify, signature, publicKey))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Invalid signature at seq {seq}"));
                    return false;
                }
            }
            else
            {
                // Fallback to AgentRegistry
                var agentRecord = _registry.GetAgent(agentId);
                if (agentRecord == null)
                {
                    // Unknown agent — cannot verify signature
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Unknown agent at seq {seq}"));
                    return false; 
                }
                
                // F7: Validity Check during Verification?
                // If we are verifying HISTORY, the key might be expired NOW but was valid THEN.
                // We should check if ts is within [validStart, validEnd].
                long tsSeconds = ts / 1000000;
                if (tsSeconds < agentRecord.ValidityStart || (agentRecord.ValidityEnd > 0 && tsSeconds > agentRecord.ValidityEnd))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Agent expired at seq {seq}"));
                    return false;
                }

                if (!SharcSigner.Verify(dataToVerify, signature, agentRecord.PublicKey))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Invalid signature at seq {seq}"));
                    return false;
                }
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
                    ColumnValue.Blob(1, reader.GetBlob(3).ToArray()), // Payload
                    ColumnValue.Blob(1, reader.GetBlob(4).ToArray()), // Hash
                    ColumnValue.Blob(1, reader.GetBlob(5).ToArray()), // Prev
                    ColumnValue.Blob(1, reader.GetBlob(6).ToArray())  // Sig
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
                long ts = decoded[1].AsInt64();
                string agentId = decoded[2].AsString();
                byte[] payload = decoded[3].AsBytes().ToArray();
                byte[] payloadHash = decoded[4].AsBytes().ToArray();
                byte[] prevHash = decoded[5].AsBytes().ToArray();
                byte[] signature = decoded[6].AsBytes().ToArray();
    
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
                
                // Verify Payload Hash
                byte[] computedHash = SharcHash.Compute(payload);
                if (!computedHash.AsSpan().SequenceEqual(payloadHash))
                    throw new InvalidOperationException($"Payload integrity failure at sequence {seq}");
    
                byte[] dataToVerify = new byte[prevHash.Length + payloadHash.Length + 8];
                prevHash.CopyTo(dataToVerify, 0);
                payloadHash.CopyTo(dataToVerify, prevHash.Length);
                BinaryPrimitives.WriteInt64BigEndian(dataToVerify.AsSpan(prevHash.Length + payloadHash.Length), seq);
    
                var agent = _registry.GetAgent(agentId);
                if (agent == null)
                {
                     throw new InvalidOperationException($"Unknown agent in delta: {agentId}");
                }
                
                // F7: Validity Check
                long tsSeconds = ts / 1000000;
                if (tsSeconds < agent.ValidityStart || (agent.ValidityEnd > 0 && tsSeconds > agent.ValidityEnd))
                    throw new InvalidOperationException($"Agent key expired or not yet valid (Seq {seq})");
    
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
                
                var payloadRecord = page.Slice(cellOffset + cellHeaderSize, payloadSize);
                var decoded = _db.RecordDecoder.DecodeRecord(payloadRecord);
    
                return new LedgerEntry(
                    decoded[0].AsInt64(),
                    decoded[1].AsInt64(),
                    decoded[2].AsString(),
                    decoded[3].AsBytes().ToArray(), // Payload
                    decoded[4].AsBytes().ToArray(), // Hash
                    decoded[5].AsBytes().ToArray(), // Prev
                    decoded[6].AsBytes().ToArray()  // Sig
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
