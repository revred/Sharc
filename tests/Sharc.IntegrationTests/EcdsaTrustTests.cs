// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class EcdsaTrustTests : IDisposable
{
    private SharcDatabase? _db;

    public void Dispose()
    {
        _db?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RegisterAgent_EcdsaP256_AcceptsValidSignature()
    {
        _db = SharcDatabase.CreateInMemory();
        var registry = new AgentRegistry(_db);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var agentId = "ecdsa-agent-1";
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var info = new AgentInfo(
            AgentId: agentId,
            Class: AgentClass.User,
            PublicKey: publicKey,
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>(),
            Algorithm: SignatureAlgorithm.EcdsaP256);

        var buffer = AgentRegistry.GetVerificationBuffer(info);
        var signature = ecdsa.SignData(buffer, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        info = info with { Signature = signature };

        registry.RegisterAgent(info);

        var retrieved = registry.GetAgent(agentId);
        Assert.NotNull(retrieved);
        Assert.Equal(agentId, retrieved.AgentId);
        Assert.Equal(SignatureAlgorithm.EcdsaP256, retrieved.Algorithm);
    }

    [Fact]
    public void RegisterAgent_EcdsaP256_RejectsInvalidSignature()
    {
        _db = SharcDatabase.CreateInMemory();
        var registry = new AgentRegistry(_db);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var agentId = "ecdsa-bad-sig";
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var info = new AgentInfo(
            AgentId: agentId,
            Class: AgentClass.User,
            PublicKey: publicKey,
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: new byte[64], // Invalid: all zeros
            Algorithm: SignatureAlgorithm.EcdsaP256);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterAgent(info));
    }

    [Fact]
    public async Task AppendAndVerify_EcdsaP256_IntegrityPasses()
    {
        _db = SharcDatabase.CreateInMemory();
        var registry = new AgentRegistry(_db);
        var ledger = new LedgerManager(_db);

        // Create and register an ECDSA agent
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = new EcdsaTestSigner("ecdsa-ledger-agent", ecdsa);

        var info = new AgentInfo(
            AgentId: signer.AgentId,
            Class: AgentClass.User,
            PublicKey: signer.GetPublicKey(),
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>(),
            Algorithm: SignatureAlgorithm.EcdsaP256);

        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = signer.Sign(buffer) };
        registry.RegisterAgent(info);

        // Append a payload
        var payload = new TrustPayload(PayloadType.Text, "ecdsa-telemetry-data")
        {
            EconomicValue = 5
        };
        await ledger.AppendAsync(payload, signer);

        // Verify integrity
        Assert.True(ledger.VerifyIntegrity());
    }

    [Fact]
    public async Task MixedAlgorithm_HmacAndEcdsa_IntegrityPasses()
    {
        _db = SharcDatabase.CreateInMemory();
        var registry = new AgentRegistry(_db);
        var ledger = new LedgerManager(_db);

        // Register HMAC agent
        using var hmacSigner = new SharcSigner("hmac-mixed-agent");
        var hmacInfo = new AgentInfo(
            AgentId: hmacSigner.AgentId,
            Class: AgentClass.User,
            PublicKey: await hmacSigner.GetPublicKeyAsync(),
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>());

        var hmacBuffer = AgentRegistry.GetVerificationBuffer(hmacInfo);
        hmacInfo = hmacInfo with { Signature = await hmacSigner.SignAsync(hmacBuffer) };
        registry.RegisterAgent(hmacInfo);

        // Register ECDSA agent
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecdsaSigner = new EcdsaTestSigner("ecdsa-mixed-agent", ecdsa);

        var ecdsaInfo = new AgentInfo(
            AgentId: ecdsaSigner.AgentId,
            Class: AgentClass.User,
            PublicKey: ecdsaSigner.GetPublicKey(),
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>(),
            Algorithm: SignatureAlgorithm.EcdsaP256);

        var ecdsaBuffer = AgentRegistry.GetVerificationBuffer(ecdsaInfo);
        ecdsaInfo = ecdsaInfo with { Signature = ecdsaSigner.Sign(ecdsaBuffer) };
        registry.RegisterAgent(ecdsaInfo);

        // Append entries from both agents
        await ledger.AppendAsync(
            new TrustPayload(PayloadType.Text, "hmac-data") { EconomicValue = 1 },
            hmacSigner);

        await ledger.AppendAsync(
            new TrustPayload(PayloadType.Text, "ecdsa-data") { EconomicValue = 2 },
            ecdsaSigner);

        await ledger.AppendAsync(
            new TrustPayload(PayloadType.Text, "hmac-data-2") { EconomicValue = 3 },
            hmacSigner);

        // Verify entire chain with mixed algorithms
        Assert.True(ledger.VerifyIntegrity());
    }

    [Fact]
    public void RegisterAgent_EcdsaP256_PersistsAlgorithm()
    {
        _db = SharcDatabase.CreateInMemory();
        var registry = new AgentRegistry(_db);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var agentId = "ecdsa-persist-test";
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var info = new AgentInfo(
            AgentId: agentId,
            Class: AgentClass.User,
            PublicKey: publicKey,
            AuthorityCeiling: 50,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>(),
            Algorithm: SignatureAlgorithm.EcdsaP256);

        var buffer = AgentRegistry.GetVerificationBuffer(info);
        var signature = ecdsa.SignData(buffer, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        info = info with { Signature = signature };
        registry.RegisterAgent(info);

        // Create a fresh registry to force re-reading from DB
        var registry2 = new AgentRegistry(_db);
        var retrieved = registry2.GetAgent(agentId);

        Assert.NotNull(retrieved);
        Assert.Equal(SignatureAlgorithm.EcdsaP256, retrieved.Algorithm);
        Assert.Equal(publicKey, retrieved.PublicKey);
    }

    /// <summary>
    /// Minimal ISharcSigner implementation for ECDSA P-256 testing.
    /// </summary>
    private sealed class EcdsaTestSigner : ISharcSigner
    {
        private readonly ECDsa _ecdsa;

        public EcdsaTestSigner(string agentId, ECDsa ecdsa)
        {
            AgentId = agentId;
            _ecdsa = ecdsa;
        }

        public string AgentId { get; }
        public SignatureAlgorithm Algorithm => SignatureAlgorithm.EcdsaP256;
        public int SignatureSize => 64;

        public byte[] Sign(ReadOnlySpan<byte> data)
        {
            return _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        public bool TrySign(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten)
        {
            return _ecdsa.TrySignData(data, destination, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out bytesWritten);
        }

        public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        {
            return _ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        public byte[] GetPublicKey()
        {
            return _ecdsa.ExportSubjectPublicKeyInfo();
        }

        public Task<byte[]> SignAsync(byte[] data) => Task.FromResult(Sign(data));
        public Task<bool> VerifyAsync(byte[] data, byte[] signature) => Task.FromResult(Verify(data, signature));
        public Task<byte[]> GetPublicKeyAsync() => Task.FromResult(GetPublicKey());

        public void Dispose() { /* ECDsa lifetime managed by test */ }
    }
}
