// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Sharc.Core.Storage;
using Sharc.Crypto;

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
    /// <param name="transaction">Optional active transaction.</param>
    public void Append(TrustPayload payload, ISharcSigner signer, Transaction? transaction = null)
    {
        var rootPage = SystemStore.GetRootPage(_db.Schema, LedgerTableName);
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        // 1. Payload Serialization (Still allocates, optimization deferred)
        byte[] payloadData = payload.ToBytes();

        // 2. Rent Buffers
        byte[] payloadHashBuf = ArrayPool<byte>.Shared.Rent(32);
        byte[] dataToSignBuf = ArrayPool<byte>.Shared.Rent(32 + 32 + 8); // PrevHash + PayloadHash + Seq
        byte[] signatureBuf = ArrayPool<byte>.Shared.Rent(signer.SignatureSize);
        int agentIdByteCount = Encoding.UTF8.GetByteCount(signer.AgentId);
        byte[] agentIdBuf = ArrayPool<byte>.Shared.Rent(agentIdByteCount);
        int agentIdLen = Encoding.UTF8.GetBytes(signer.AgentId, agentIdBuf);
        ColumnValue[] columnsBuf = ArrayPool<ColumnValue>.Shared.Rent(7);
        
        try
        {
            var lastEntry = GetLastEntry((int)rootPage);
            long nextSequence = (lastEntry?.Sequence ?? 0) + 1;
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000; 

            // 3. Compute Payload Hash
            if (!SharcHash.TryCompute(payloadData, payloadHashBuf, out int hashLen) || hashLen != 32)
                throw new InvalidOperationException("Hash computation failed.");

            // 4. Prepare Data to Sign (PrevHash + PayloadHash + Seq)
            Span<byte> dataToSignSpan = dataToSignBuf.AsSpan(0, 72);
            
            // Copy PrevHash (or zero if genesis)
            if (lastEntry != null)
                lastEntry.Value.PayloadHash.AsSpan().CopyTo(dataToSignSpan.Slice(0, 32));
            else
                dataToSignSpan.Slice(0, 32).Clear();

            // Copy PayloadHash
            payloadHashBuf.AsSpan(0, 32).CopyTo(dataToSignSpan.Slice(32, 32));
            
            // Copy Sequence
            BinaryPrimitives.WriteInt64BigEndian(dataToSignSpan.Slice(64, 8), nextSequence);

            // 5. Validate Agent
            var agentInfo = _registry.GetAgent(signer.AgentId);
            if (agentInfo != null) ValidateAgent(agentInfo, payload, signer, timestamp);

            // 6. Sign
            if (!signer.TrySign(dataToSignSpan, signatureBuf, out int sigLen))
                 throw new InvalidOperationException("Signing failed.");

            // 7. Build Columns
            columnsBuf[0] = ColumnValue.FromInt64(1, nextSequence);
            columnsBuf[1] = ColumnValue.FromInt64(2, timestamp);
            columnsBuf[2] = ColumnValue.Text(13 + 2 * agentIdLen, agentIdBuf.AsMemory(0, agentIdLen));
            columnsBuf[3] = ColumnValue.Blob(4, payloadData);
            columnsBuf[4] = ColumnValue.Blob(5, payloadHashBuf.AsMemory(0, 32));
            columnsBuf[5] = ColumnValue.Blob(6, dataToSignBuf.AsMemory(0, 32)); // PrevHash
            columnsBuf[6] = ColumnValue.Blob(7, signatureBuf.AsMemory(0, sigLen));
    
            // 8. Insert Record (Zero-Alloc Overload)
            SystemStore.InsertRecord(
                tx.GetShadowSource(), 
                _db.Header.UsablePageSize, 
                rootPage, 
                nextSequence, 
                columnsBuf.AsSpan(0, 7));

            if (ownsTransaction) tx.Commit();
            SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendSuccess, signer.AgentId, $"seq={nextSequence}"));
        }
        finally
        {
            // Return buffers
            ArrayPool<byte>.Shared.Return(payloadHashBuf);
            ArrayPool<byte>.Shared.Return(dataToSignBuf);
            ArrayPool<byte>.Shared.Return(signatureBuf);
            ArrayPool<byte>.Shared.Return(agentIdBuf);
            ArrayPool<ColumnValue>.Shared.Return(columnsBuf, clearArray: true);

            if (ownsTransaction) tx.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously appends a structured trust payload to the ledger.
    /// </summary>
    public async Task AppendAsync(TrustPayload payload, ISharcSigner signer, Transaction? transaction = null)
    {
        var rootPage = SystemStore.GetRootPage(_db.Schema, LedgerTableName);
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        byte[] payloadData = payload.ToBytes();

        byte[] payloadHashBuf = ArrayPool<byte>.Shared.Rent(32);
        byte[] dataToSignBuf = ArrayPool<byte>.Shared.Rent(32 + 32 + 8); 
        byte[] signatureBuf = ArrayPool<byte>.Shared.Rent(signer.SignatureSize);
        int agentIdByteCount = Encoding.UTF8.GetByteCount(signer.AgentId);
        byte[] agentIdBuf = ArrayPool<byte>.Shared.Rent(agentIdByteCount);
        int agentIdLen = Encoding.UTF8.GetBytes(signer.AgentId, agentIdBuf);
        ColumnValue[] columnsBuf = ArrayPool<ColumnValue>.Shared.Rent(7);
        
        try
        {
            var lastEntry = GetLastEntry((int)rootPage);
            long nextSequence = (lastEntry?.Sequence ?? 0) + 1;
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000; 

            if (!SharcHash.TryCompute(payloadData, payloadHashBuf, out int hashLen) || hashLen != 32)
                throw new InvalidOperationException("Hash computation failed.");

            Span<byte> dataToSignSpan = dataToSignBuf.AsSpan(0, 72);
            if (lastEntry != null)
                lastEntry.Value.PayloadHash.AsSpan().CopyTo(dataToSignSpan.Slice(0, 32));
            else
                dataToSignSpan.Slice(0, 32).Clear();

            payloadHashBuf.AsSpan(0, 32).CopyTo(dataToSignSpan.Slice(32, 32));
            BinaryPrimitives.WriteInt64BigEndian(dataToSignSpan.Slice(64, 8), nextSequence);

            var agentInfo = _registry.GetAgent(signer.AgentId);
            if (agentInfo != null) ValidateAgent(agentInfo, payload, signer, timestamp);

            // Await async signatures instead of mocking or blocking
            byte[] signature = await signer.SignAsync(dataToSignBuf.AsSpan(0, 72).ToArray());
            int sigLen = signature.Length;
            signature.CopyTo(signatureBuf, 0);

            columnsBuf[0] = ColumnValue.FromInt64(1, nextSequence);
            columnsBuf[1] = ColumnValue.FromInt64(2, timestamp);
            columnsBuf[2] = ColumnValue.Text(13 + 2 * agentIdLen, agentIdBuf.AsMemory(0, agentIdLen));
            columnsBuf[3] = ColumnValue.Blob(4, payloadData);
            columnsBuf[4] = ColumnValue.Blob(5, payloadHashBuf.AsMemory(0, 32));
            columnsBuf[5] = ColumnValue.Blob(6, dataToSignBuf.AsMemory(0, 32));
            columnsBuf[6] = ColumnValue.Blob(7, signatureBuf.AsMemory(0, sigLen));
    
            SystemStore.InsertRecord(
                tx.GetShadowSource(), 
                _db.Header.UsablePageSize, 
                rootPage, 
                nextSequence, 
                columnsBuf.AsSpan(0, 7));

            if (ownsTransaction) tx.Commit();
            SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.AppendSuccess, signer.AgentId, $"seq={nextSequence}"));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payloadHashBuf);
            ArrayPool<byte>.Shared.Return(dataToSignBuf);
            ArrayPool<byte>.Shared.Return(signatureBuf);
            ArrayPool<byte>.Shared.Return(agentIdBuf);
            ArrayPool<ColumnValue>.Shared.Return(columnsBuf, clearArray: true);

            if (ownsTransaction) tx.Dispose();
        }
    }

    private void ValidateAgent(AgentInfo agent, TrustPayload payload, ISharcSigner signer, long timestamp)
    {
        long tsSeconds = timestamp / 1000000;
        if (tsSeconds < agent.ValidityStart || (agent.ValidityEnd > 0 && tsSeconds > agent.ValidityEnd))
            throw new InvalidOperationException("Agent key is not valid.");

        if (payload.EconomicValue > agent.AuthorityCeiling)
            throw new InvalidOperationException($"Authority ceiling exceeded. limit: {agent.AuthorityCeiling}");

        if (agent.CoSignRequired) VerifyCoSignatures(agent, payload, signer);
    }

    private void VerifyCoSignatures(AgentInfo agent, TrustPayload payload, ISharcSigner primary)
    {
        if (payload.CoSignatures == null || payload.CoSignatures.Count == 0)
            throw new InvalidOperationException("Co-signature required.");

        var baseData = (payload with { CoSignatures = null }).ToBytes();
        Span<byte> baseHash = stackalloc byte[32];
        SharcHash.TryCompute(baseData, baseHash, out _);

        Span<byte> signedData = stackalloc byte[40]; // hash(32) + timestamp(8)
        foreach (var sig in payload.CoSignatures)
        {
            var coSigner = _registry.GetAgent(sig.SignerId) ?? throw new InvalidOperationException("Unknown co-signer.");
            if (sig.SignerId == primary.AgentId) throw new InvalidOperationException("Cannot self-cosign.");

            baseHash.CopyTo(signedData);
            BinaryPrimitives.WriteInt64BigEndian(signedData.Slice(32), sig.Timestamp);

            if (!SignatureVerifier.Verify(signedData, sig.Signature, coSigner.PublicKey, coSigner.Algorithm))
                throw new InvalidOperationException("Invalid co-signature.");
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
        
        // Reusable stack buffers — eliminate per-row heap allocations
        Span<byte> computedHash = stackalloc byte[32];
        Span<byte> dataToVerify = stackalloc byte[72]; // prevHash(32) + payloadHash(32) + seq(8)
        Span<byte> coSigHash = stackalloc byte[32];    // for co-signature verification (cold path)
        Span<byte> coSigData = stackalloc byte[40];    // hash(32) + timestamp(8)
        byte[] expectedPrevHash = new byte[32]; // carries across rows
        long expectedSequence = 1;

        while (cursor.Read())
        {
            long seq = cursor.GetInt64(0);
            long ts = cursor.GetInt64(1);
            string agentId = cursor.GetString(2);

            // Zero-copy blob access — spans point directly into the page buffer
            var payloadSpan = cursor.GetBlobSpan(3);
            var payloadHashSpan = cursor.GetBlobSpan(4);
            var prevHashSpan = cursor.GetBlobSpan(5);
            var signatureSpan = cursor.GetBlobSpan(6);

            // 0. Verify Payload Hash
            if (!SharcHash.TryCompute(payloadSpan, computedHash, out _) ||
                !computedHash.SequenceEqual(payloadHashSpan))
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Payload hash mismatch at seq {seq}"));
                return false;
            }

            // 0.1 Verify Co-Signatures (cold path — allocations acceptable for deserialization)
            try
            {
                var trustPayload = TrustPayload.FromBytes(payloadSpan);
                if (trustPayload?.CoSignatures is { Count: > 0 })
                {
                    var basePayload = trustPayload with { CoSignatures = null };
                    SharcHash.TryCompute(basePayload.ToBytes(), coSigHash, out _);

                    foreach (var sig in trustPayload.CoSignatures)
                    {
                       var coSigner = _registry.GetAgent(sig.SignerId);
                       if (coSigner == null)
                       {
                           SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, sig.SignerId, $"Unknown co-signer at seq {seq}"));
                           return false;
                       }

                       coSigHash.CopyTo(coSigData);
                       BinaryPrimitives.WriteInt64BigEndian(coSigData.Slice(32), sig.Timestamp);

                       if (!SignatureVerifier.Verify(coSigData, sig.Signature, coSigner.PublicKey, coSigner.Algorithm))
                       {
                           SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, sig.SignerId, $"Invalid co-signature at seq {seq}"));
                           return false;
                       }
                    }
                }
            }
            catch
            {
                // Not a TrustPayload or deserialization failed — allow pass for generic BLOB support.
            }

            // 1. Check sequence
            if (seq != expectedSequence) 
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Sequence gap: {expectedSequence} -> {seq}"));
                return false;
            }

            // 2. Check previous hash link
            if (!prevHashSpan.SequenceEqual(expectedPrevHash))
            {
                SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, "System", $"Hash chain broken at seq {seq}"));
                return false;
            }

            // 3. Verify signature (Who signed What and When)
            prevHashSpan.CopyTo(dataToVerify.Slice(0, 32));
            payloadHashSpan.CopyTo(dataToVerify.Slice(32, 32));
            BinaryPrimitives.WriteInt64BigEndian(dataToVerify.Slice(64, 8), seq);

            // Resolve algorithm: look up agent record to determine signature algorithm
            var agentRecord = _registry.GetAgent(agentId);
            var sigAlgorithm = agentRecord?.Algorithm ?? SignatureAlgorithm.HmacSha256;

            if (activeSigners != null && activeSigners.TryGetValue(agentId, out var publicKey))
            {
                if (!SignatureVerifier.Verify(dataToVerify, signatureSpan, publicKey, sigAlgorithm))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Invalid signature at seq {seq}"));
                    return false;
                }
            }
            else
            {
                if (agentRecord == null)
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Unknown agent at seq {seq}"));
                    return false;
                }

                long tsSeconds = ts / 1000000;
                if (tsSeconds < agentRecord.ValidityStart || (agentRecord.ValidityEnd > 0 && tsSeconds > agentRecord.ValidityEnd))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Agent expired at seq {seq}"));
                    return false;
                }

                if (!SignatureVerifier.Verify(dataToVerify, signatureSpan, agentRecord.PublicKey, sigAlgorithm))
                {
                    SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.IntegrityViolation, agentId, $"Invalid signature at seq {seq}"));
                    return false;
                }
            }

            payloadHashSpan.CopyTo(expectedPrevHash);
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
                    ColumnValue.Blob(1, reader.GetBlob(3)), // Payload
                    ColumnValue.Blob(1, reader.GetBlob(4)), // Hash
                    ColumnValue.Blob(1, reader.GetBlob(5)), // Prev
                    ColumnValue.Blob(1, reader.GetBlob(6))  // Sig
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
    /// Imports a sequence of ledger deltas into the local ledger.
    /// </summary>
    /// <param name="deltas">Enumerable of raw record payloads.</param>
    /// <param name="transaction">Optional active transaction.</param>
    public void ImportDeltas(IEnumerable<byte[]> deltas, Transaction? transaction = null)
    {
        var rootPage = SystemStore.GetRootPage(_db.Schema, LedgerTableName);
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();
        
        try
        {
            foreach (var delta in deltas)
            {
                var entry = SystemStore.Decode(delta, _db.RecordDecoder, v => new LedgerEntry(
                    v[0].AsInt64(), v[1].AsInt64(), v[2].AsString(), v[3].AsBytes().ToArray(),
                    v[4].AsBytes().ToArray(), v[5].AsBytes().ToArray(), v[6].AsBytes().ToArray()));

                var last = GetLastEntry((int)rootPage);
                if (last != null && (entry.SequenceNumber != last.Value.Sequence + 1 ||
                    !entry.PreviousHash.AsSpan().SequenceEqual(last.Value.PayloadHash)))
                    throw new InvalidOperationException("Chain break.");
                
                SystemStore.InsertRecord(tx.GetShadowSource(), _db.Header.UsablePageSize, rootPage, entry.SequenceNumber, RecordEncoder.ToColumnValues(delta, _db.RecordDecoder));
            }
            if (ownsTransaction) tx.Commit();
        }
        finally { if (ownsTransaction) tx.Dispose(); }
    }

    private (long Sequence, byte[] PayloadHash)? GetLastEntry(int rootPage)
    {
        uint pageNum = (uint)rootPage;

        while (true)
        {
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

                // Targeted decode: only extract columns 0 (seq) and 4 (payloadHash).
                // Avoids ColumnValue[7] allocation + 5 unused blob/text ToArray() copies.
                Span<long> serialTypes = stackalloc long[7];
                _db.RecordDecoder.ReadSerialTypes(payloadRecord, serialTypes, out int bodyOffset);

                // Column 0: sequence number — zero-alloc inline decode
                long seq = _db.RecordDecoder.DecodeInt64Direct(payloadRecord, 0, serialTypes, bodyOffset);

                // Column 4: payloadHash — compute offset, copy blob directly from page span
                Span<int> offsets = stackalloc int[5];
                _db.RecordDecoder.ComputeColumnOffsets(serialTypes, 5, bodyOffset, offsets);
                int hashSize = (int)((serialTypes[4] - 12) / 2); // blob serial type size formula
                byte[] hash = payloadRecord.Slice(offsets[4], hashSize).ToArray();

                return (seq, hash);
            }
            else
            {
                pageNum = header.RightChildPage;
            }
        }
    }
}
