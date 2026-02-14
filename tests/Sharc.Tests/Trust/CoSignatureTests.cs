using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
// Using Sharc.Crypto.SharcSigner 
// Check namespaces
using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Trust;
using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Trust;

public class CoSignatureTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private readonly AgentRegistry _registry;
    private readonly LedgerManager _ledger;

    public CoSignatureTests()
    {
        _dbPath = Path.GetTempFileName();
        var data = TrustTestFixtures.CreateTrustDatabase();
        File.WriteAllBytes(_dbPath, data);
        
        _db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        _registry = new AgentRegistry(_db);
        _ledger = new LedgerManager(_db);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Append_CoSignRequired_ButMissing_Throws()
    {
        // 1. Register Agent requiring Co-Sign
        var signer = new SharcSigner("primary-agent");
        var agentInfo = CreateAgent(signer, coSignRequired: true);
        _registry.RegisterAgent(agentInfo);

        // 2. Try Append without Co-Signatures
        var payload = new TrustPayload(PayloadType.Text, "check me");
        
        Action act = () => _ledger.Append(payload, signer);
        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Co-signature required.", ex.Message);
    }

    [Fact]
    public void Append_WithValidCoSignature_Succeeds()
    {
        // 1. Register Primary Agent (Requires Co-Sign)
        var primarySigner = new SharcSigner("primary");
        var primaryAgent = CreateAgent(primarySigner, coSignRequired: true);
        _registry.RegisterAgent(primaryAgent);

        // 2. Register Co-Signer
        var coSignerKey = new SharcSigner("cosigner");
        var coSignerAgent = CreateAgent(coSignerKey, coSignRequired: false);
        _registry.RegisterAgent(coSignerAgent);

        // 3. Prepare Payload
        // Create base payload first
        var basePayload = new TrustPayload(PayloadType.Text, "approved content");
        var baseData = basePayload.ToBytes();
        var baseHash = SharcHash.Compute(baseData);
        
        // 4. Co-Sign Logic
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] dataToSign = new byte[baseHash.Length + 8];
        baseHash.CopyTo(dataToSign, 0);
        BinaryPrimitives.WriteInt64BigEndian(dataToSign.AsSpan(baseHash.Length), timestamp);
        
        var coSigBytes = coSignerKey.Sign(dataToSign);
        
        var coSignature = new CoSignature(coSignerKey.AgentId, coSigBytes, timestamp);
        
        // 5. Create final payload
        var finalPayload = basePayload with { CoSignatures = new List<CoSignature> { coSignature } };
        
        // 6. Append
        _ledger.Append(finalPayload, primarySigner);
        
        // 7. Verify Integrity
        Assert.True(_ledger.VerifyIntegrity());
    }
    
    // Helper to avoid duplicate logic
    private static AgentInfo CreateAgent(SharcSigner signer, bool coSignRequired)
    {
        var pub = signer.GetPublicKey();
        var wScope = "*";
        var rScope = "*";
        var parent = "";
        
        int bufferSize = Encoding.UTF8.GetByteCount(signer.AgentId) + 1 + pub.Length + 8 + 
                         Encoding.UTF8.GetByteCount(wScope) + Encoding.UTF8.GetByteCount(rScope) + 
                         8 + 8 + Encoding.UTF8.GetByteCount(parent) + 1;
        
        byte[] data = new byte[bufferSize];
        int offset = 0;
        offset += Encoding.UTF8.GetBytes(signer.AgentId, data.AsSpan(offset));
        data[offset++] = (byte)AgentClass.User;
        pub.CopyTo(data.AsSpan(offset));
        offset += pub.Length;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), ulong.MaxValue);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(wScope, data.AsSpan(offset));
        offset += Encoding.UTF8.GetBytes(rScope, data.AsSpan(offset));
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), 0);
        offset += 8;
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), 0);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(parent, data.AsSpan(offset));
        data[offset++] = coSignRequired ? (byte)1 : (byte)0;
        
        var sig = signer.Sign(data);
        return new AgentInfo(signer.AgentId, AgentClass.User, pub, ulong.MaxValue, wScope, rScope, 0, 0, parent, coSignRequired, sig);
    }
}
