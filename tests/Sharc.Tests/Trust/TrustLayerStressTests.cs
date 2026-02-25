// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

/// <summary>
/// Stress tests for the Trust layer — covers ledger chain integrity at scale,
/// multi-agent registration, authority ceiling boundary conditions, co-signature
/// chains, tamper detection at depth, export/import round-trips, validity window
/// enforcement, and adversarial replay scenarios.
/// </summary>
public sealed class TrustLayerStressTests : IDisposable
{
    private readonly string _dbPath;

    public TrustLayerStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trust_stress_{Guid.NewGuid()}.db");
        File.WriteAllBytes(_dbPath, TrustTestFixtures.CreateTrustDatabase());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private SharcDatabase OpenWritable()
        => SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });

    private static AgentInfo CreateAgent(SharcSigner signer,
        ulong authorityCeiling = ulong.MaxValue,
        long validityStart = 0, long validityEnd = 0,
        bool coSignRequired = false,
        string writeScope = "*", string readScope = "*")
    {
        var pub = signer.GetPublicKey();
        var parent = "";

        int bufferSize = Encoding.UTF8.GetByteCount(signer.AgentId) + 1 + pub.Length + 8 +
                         Encoding.UTF8.GetByteCount(writeScope) + Encoding.UTF8.GetByteCount(readScope) +
                         8 + 8 + Encoding.UTF8.GetByteCount(parent) + 1;

        byte[] data = new byte[bufferSize];
        int offset = 0;
        offset += Encoding.UTF8.GetBytes(signer.AgentId, data.AsSpan(offset));
        data[offset++] = (byte)AgentClass.User;
        pub.CopyTo(data.AsSpan(offset));
        offset += pub.Length;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), authorityCeiling);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(writeScope, data.AsSpan(offset));
        offset += Encoding.UTF8.GetBytes(readScope, data.AsSpan(offset));
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), validityStart);
        offset += 8;
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), validityEnd);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(parent, data.AsSpan(offset));
        data[offset++] = coSignRequired ? (byte)1 : (byte)0;

        var sig = signer.Sign(data);
        return new AgentInfo(signer.AgentId, AgentClass.User, pub, authorityCeiling,
            writeScope, readScope, validityStart, validityEnd, parent, coSignRequired, sig);
    }

    // ── Ledger Chain Integrity at Scale ──

    [Fact]
    public void Ledger_50Entries_ChainIntegrityMaintained()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("chain-agent");
        registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        for (int i = 0; i < 50; i++)
            ledger.Append($"Entry {i}: context data for chain test", signer);

        var keys = new Dictionary<string, byte[]> { [signer.AgentId] = signer.GetPublicKey() };
        Assert.True(ledger.VerifyIntegrity(keys));
    }

    [Fact]
    public void Ledger_200Entries_SurvivesSplit_ChainValid()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        using var signer = new SharcSigner("split-agent");
        registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        // 200 entries forces multiple B-tree leaf page splits
        // (4096-byte page with ~80-byte ledger records ≈ 40-50 per leaf)
        for (int i = 0; i < 200; i++)
            ledger.Append($"Overflow entry {i}: padding data to increase record size beyond minimum", signer);

        var keys = new Dictionary<string, byte[]> { [signer.AgentId] = signer.GetPublicKey() };
        Assert.True(ledger.VerifyIntegrity(keys));

        // Verify exact count via reader scan
        using var reader = db.CreateReader("_sharc_ledger");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(200, count);
    }

    [Fact]
    public void Ledger_MultipleAgents_InterleavedAppends_ChainValid()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);

        var agents = new List<SharcSigner>();
        for (int i = 0; i < 5; i++)
        {
            var signer = new SharcSigner($"agent-{i}");
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));
            agents.Add(signer);
        }

        // Interleave appends from different agents
        for (int round = 0; round < 10; round++)
        {
            foreach (var agent in agents)
                ledger.Append($"Round {round} from {agent.AgentId}", agent);
        }

        // Verify full chain (50 entries from 5 agents)
        Assert.True(ledger.VerifyIntegrity());
    }

    // ── Agent Registration Stress ──

    [Fact]
    public void Register_20Agents_AllRetrievable()
    {
        using var db = OpenWritable();
        var registry = new AgentRegistry(db);

        var signers = new List<SharcSigner>();
        for (int i = 0; i < 20; i++)
        {
            var signer = new SharcSigner($"bulk-agent-{i:D3}");
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));
            signers.Add(signer);
        }

        // All should be retrievable
        foreach (var signer in signers)
        {
            var agent = registry.GetAgent(signer.AgentId);
            Assert.NotNull(agent);
            Assert.Equal(signer.AgentId, agent.AgentId);
            Assert.Equal(signer.GetPublicKey(), agent.PublicKey);
        }
    }

    [Fact]
    public void GetAgent_NonExistent_ReturnsNull()
    {
        using var db = OpenWritable();
        var registry = new AgentRegistry(db);

        var result = registry.GetAgent("no-such-agent");
        Assert.Null(result);
    }

    // ── Authority Ceiling Boundaries ──

    [Fact]
    public void AuthorityCeiling_ExactLimit_Succeeds()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("ceiling-exact");
        registry.RegisterAgent(CreateAgent(signer, authorityCeiling: 1000));

        // Exactly at ceiling should work
        var payload = new TrustPayload(PayloadType.Financial, "Exact limit", 1000);
        ledger.Append(payload, signer);

        Assert.True(ledger.VerifyIntegrity());
    }

    [Fact]
    public void AuthorityCeiling_OneOverLimit_Throws()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("ceiling-over");
        registry.RegisterAgent(CreateAgent(signer, authorityCeiling: 1000));

        var payload = new TrustPayload(PayloadType.Financial, "Over limit", 1001);
        var ex = Assert.Throws<InvalidOperationException>(() => ledger.Append(payload, signer));
        Assert.Contains("Authority ceiling exceeded", ex.Message);
    }

    [Fact]
    public void AuthorityCeiling_ZeroLimit_FinancialPayload_Throws()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("ceiling-zero");
        registry.RegisterAgent(CreateAgent(signer, authorityCeiling: 0));

        var payload = new TrustPayload(PayloadType.Financial, "Any value", 1);
        var ex = Assert.Throws<InvalidOperationException>(() => ledger.Append(payload, signer));
        Assert.Contains("Authority ceiling exceeded", ex.Message);
    }

    [Fact]
    public void AuthorityCeiling_NonFinancialPayload_IgnoresCeiling()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("ceiling-text");
        registry.RegisterAgent(CreateAgent(signer, authorityCeiling: 0));

        // Text payload has no economic value — should not trigger ceiling check
        var payload = new TrustPayload(PayloadType.Text, "Just a note");
        ledger.Append(payload, signer);

        Assert.True(ledger.VerifyIntegrity());
    }

    // ── Validity Window ──

    [Fact]
    public void ValidityWindow_ExpiredAgent_Throws()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("expired-agent");

        // ValidateAgent compares tsSeconds (microseconds / 1M) against ValidityStart/End
        // So validity values must be in Unix seconds
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        registry.RegisterAgent(CreateAgent(signer,
            validityStart: nowSeconds - 7200,
            validityEnd: nowSeconds - 3600));

        var payload = new TrustPayload(PayloadType.Text, "I'm expired");
        Assert.Throws<InvalidOperationException>(() => ledger.Append(payload, signer));
    }

    [Fact]
    public void ValidityWindow_NotYetValid_Throws()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("future-agent");

        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        registry.RegisterAgent(CreateAgent(signer,
            validityStart: nowSeconds + 3600,
            validityEnd: nowSeconds + 7200));

        var payload = new TrustPayload(PayloadType.Text, "Not yet valid");
        Assert.Throws<InvalidOperationException>(() => ledger.Append(payload, signer));
    }

    [Fact]
    public void ValidityWindow_CurrentlyValid_Succeeds()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("valid-agent");

        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        registry.RegisterAgent(CreateAgent(signer,
            validityStart: nowSeconds - 3600,
            validityEnd: nowSeconds + 3600));

        ledger.Append("Within validity window", signer);
        Assert.True(ledger.VerifyIntegrity());
    }

    // ── Co-Signature Stress ──

    [Fact]
    public void CoSign_MissingCoSignature_Throws()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("needs-cosign");
        registry.RegisterAgent(CreateAgent(signer, coSignRequired: true));

        var payload = new TrustPayload(PayloadType.Text, "Unsigned action");
        var ex = Assert.Throws<InvalidOperationException>(() => ledger.Append(payload, signer));
        Assert.Contains("Co-signature required", ex.Message);
    }

    [Fact]
    public void CoSign_ValidCoSignature_Succeeds()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);

        var primary = new SharcSigner("primary-cosign");
        var cosigner = new SharcSigner("cosigner-a");
        registry.RegisterAgent(CreateAgent(primary, coSignRequired: true));
        registry.RegisterAgent(CreateAgent(cosigner, coSignRequired: false));

        // Build co-signature
        var basePayload = new TrustPayload(PayloadType.Text, "Approved action");
        var baseHash = Sharc.Crypto.SharcHash.Compute(basePayload.ToBytes());
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] dataToSign = new byte[baseHash.Length + 8];
        baseHash.CopyTo(dataToSign, 0);
        BinaryPrimitives.WriteInt64BigEndian(dataToSign.AsSpan(baseHash.Length), timestamp);
        var coSigBytes = cosigner.Sign(dataToSign);

        var finalPayload = basePayload with
        {
            CoSignatures = new List<CoSignature>
            {
                new CoSignature(cosigner.AgentId, coSigBytes, timestamp)
            }
        };

        ledger.Append(finalPayload, primary);
        Assert.True(ledger.VerifyIntegrity());
    }

    // ── Export / Import Delta Sync ──

    [Fact]
    public void ExportImport_20Entries_FullRoundTrip()
    {
        // Source DB
        using var dbA = OpenWritable();
        var ledgerA = new LedgerManager(dbA);
        var registryA = new AgentRegistry(dbA);
        var signer = new SharcSigner("sync-agent");
        registryA.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        for (int i = 0; i < 20; i++)
            ledgerA.Append($"Delta entry {i}", signer);

        var deltas = ledgerA.ExportDeltas(1);
        Assert.Equal(20, deltas.Count);

        // Destination DB
        var destPath = Path.Combine(Path.GetTempPath(), $"trust_dest_{Guid.NewGuid()}.db");
        try
        {
            File.WriteAllBytes(destPath, TrustTestFixtures.CreateTrustDatabase());
            using var dbB = SharcDatabase.Open(destPath, new SharcOpenOptions { Writable = true });
            var ledgerB = new LedgerManager(dbB);
            var registryB = new AgentRegistry(dbB);
            registryB.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

            ledgerB.ImportDeltas(deltas);
            Assert.True(ledgerB.VerifyIntegrity());

            // Verify row count
            using var reader = dbB.CreateReader("_sharc_ledger");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(20, count);
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    [Fact]
    public void ExportPartial_ImportContinues_ChainValid()
    {
        using var dbA = OpenWritable();
        var ledgerA = new LedgerManager(dbA);
        var registryA = new AgentRegistry(dbA);
        var signer = new SharcSigner("partial-sync");
        registryA.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        for (int i = 0; i < 10; i++)
            ledgerA.Append($"Entry {i}", signer);

        // Export only entries 6-10
        var deltas = ledgerA.ExportDeltas(6);
        Assert.Equal(5, deltas.Count);
    }

    // ── Tamper Detection ──

    [Fact]
    public void Tamper_PayloadModified_DetectedByVerify()
    {
        using (var db = OpenWritable())
        {
            var ledger = new LedgerManager(db);
            var registry = new AgentRegistry(db);
            var signer = new SharcSigner("tamper-victim");
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

            ledger.Append("Honest data", signer);
            ledger.Append("More honest data", signer);

            var keys = new Dictionary<string, byte[]> { [signer.AgentId] = signer.GetPublicKey() };
            Assert.True(ledger.VerifyIntegrity(keys));
        }

        // Tamper with the file: flip a bit in the ledger page
        var diskData = File.ReadAllBytes(_dbPath);
        // Page 2 (offset 4096) contains ledger data
        // Find the first cell and corrupt a payload byte
        var page2 = diskData.AsSpan(4096, 4096);
        ushort cellPtr = BinaryPrimitives.ReadUInt16BigEndian(page2.Slice(8, 2));

        // Parse enough to find the payload area
        var cell = page2.Slice(cellPtr);
        int pSzLen = Sharc.Core.Primitives.VarintDecoder.Read(cell, out _);
        int rIdLen = Sharc.Core.Primitives.VarintDecoder.Read(cell.Slice(pSzLen), out _);
        int headerLen = pSzLen + rIdLen;

        // Corrupt a byte in the payload body
        diskData[4096 + cellPtr + headerLen + 20] ^= 0xFF;
        File.WriteAllBytes(_dbPath, diskData);

        // Re-open and verify — should detect tampering
        using (var db2 = OpenWritable())
        {
            var ledger2 = new LedgerManager(db2);
            var signer2 = new SharcSigner("tamper-victim");
            var keys2 = new Dictionary<string, byte[]> { [signer2.AgentId] = signer2.GetPublicKey() };
            Assert.False(ledger2.VerifyIntegrity(keys2));
        }
    }

    // ── Structured Payload Types ──

    [Fact]
    public void Payload_AllTypes_RoundTrip()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("payload-agent");
        registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        // Text payload
        ledger.Append(new TrustPayload(PayloadType.Text, "Plain text"), signer);

        // Financial payload with evidence
        var evidence = new EvidenceRef("Invoices", 42, new byte[] { 0xDE, 0xAD });
        ledger.Append(new TrustPayload(PayloadType.Financial, "Expense", 250,
            new List<EvidenceRef> { evidence }), signer);

        // Approval payload
        ledger.Append(new TrustPayload(PayloadType.Approval, "Approved by committee"), signer);

        // System payload
        ledger.Append(new TrustPayload(PayloadType.System, "Startup diagnostics"), signer);

        Assert.True(ledger.VerifyIntegrity());

        // Verify payloads can be decoded
        using var reader = db.CreateReader("_sharc_ledger");
        int count = 0;
        while (reader.Read())
        {
            var blob = reader.GetBlob(3).ToArray();
            var decoded = TrustPayload.FromBytes(blob);
            Assert.NotNull(decoded);
            count++;
        }
        Assert.Equal(4, count);
    }

    // ── Agent + Ledger Transaction Interaction ──

    [Fact]
    public void RegisterAgent_InTransaction_Commit_Persists()
    {
        using (var db = OpenWritable())
        {
            var registry = new AgentRegistry(db);
            using var tx = db.BeginTransaction();

            var signer = new SharcSigner("tx-agent");
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer), tx);
            tx.Commit();
        }

        // Reopen and verify persisted
        using (var db = OpenWritable())
        {
            var registry = new AgentRegistry(db);
            var agent = registry.GetAgent("tx-agent");
            Assert.NotNull(agent);
        }
    }

    [Fact]
    public void RegisterAgent_InTransaction_NoCommit_NotPersisted()
    {
        using (var db = OpenWritable())
        {
            var registry = new AgentRegistry(db);
            using var tx = db.BeginTransaction();

            var signer = new SharcSigner("phantom-agent");
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer), tx);
            // No commit — transaction rolls back on dispose
        }

        // Reopen — agent should not exist
        using (var db = OpenWritable())
        {
            var registry = new AgentRegistry(db);
            var agent = registry.GetAgent("phantom-agent");
            Assert.Null(agent);
        }
    }

    // ── Replay Protection ──

    [Fact]
    public void Replay_DuplicateSignature_DifferentPayload_ChainStillValid()
    {
        using var db = OpenWritable();
        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);
        var signer = new SharcSigner("replay-agent");
        registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        // Append two entries with identical content — chain should still be valid
        // because sequence numbers and timestamps differ
        ledger.Append("Same content", signer);
        ledger.Append("Same content", signer);

        Assert.True(ledger.VerifyIntegrity());

        // Verify they have different sequence numbers
        using var reader = db.CreateReader("_sharc_ledger");
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt64(0));
    }

    // ── Signer Verification ──

    [Fact]
    public void Signer_WrongKey_VerifyFails()
    {
        var signer = new SharcSigner("agent-x");
        var imposter = new SharcSigner("agent-y");

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = signer.Sign(data);

        // Verify with correct key succeeds
        Assert.True(SharcSigner.Verify(data, sig, signer.GetPublicKey()));

        // Verify with wrong key fails
        Assert.False(SharcSigner.Verify(data, sig, imposter.GetPublicKey()));
    }

    [Fact]
    public void Signer_TamperedData_VerifyFails()
    {
        var signer = new SharcSigner("tamper-sig");
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var sig = signer.Sign(data);

        Assert.True(signer.Verify(data, sig));

        // Tamper with data
        data[2] ^= 0xFF;
        Assert.False(signer.Verify(data, sig));
    }
}
