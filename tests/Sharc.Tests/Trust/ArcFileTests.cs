// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arena.Wasm.Services;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public sealed class ArcFileTests
{
    [Fact]
    public void CreateInMemory_ReturnsValidArc()
    {
        using var arc = ArcFile.CreateInMemory("test.arc");

        Assert.Equal("test.arc", arc.Name);
        Assert.NotNull(arc.Database);
        Assert.NotNull(arc.Ledger);
        Assert.NotNull(arc.Registry);
        Assert.False(arc.IsDurable);
        Assert.False(arc.IsOpfsBacked);
        Assert.Equal(0, arc.LedgerEntryCount);
    }

    [Fact]
    public void CreateInMemory_WithDurableFlag_SetsDurable()
    {
        using var arc = ArcFile.CreateInMemory("durable.arc", durable: true);

        Assert.True(arc.IsDurable);
    }

    [Fact]
    public async Task RegisterAgent_AgentRetrievableFromRegistry()
    {
        using var arc = ArcFile.CreateInMemory("test.arc");
        using var signer = new SharcSigner("test-agent");
        var pubKey = await signer.GetPublicKeyAsync();

        var info = new AgentInfo("test-agent", AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };

        arc.RegisterAgent(info);

        var retrieved = arc.Registry.GetAgent("test-agent");
        Assert.NotNull(retrieved);
        Assert.Equal("test-agent", retrieved.AgentId);
    }

    [Fact]
    public async Task AppendAsync_IncreasesLedgerEntryCount()
    {
        using var arc = ArcFile.CreateInMemory("test.arc");
        using var signer = new SharcSigner("test-agent");
        var pubKey = await signer.GetPublicKeyAsync();

        var info = new AgentInfo("test-agent", AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };
        arc.RegisterAgent(info);

        var payload = new TrustPayload(PayloadType.Text, "sensor-data") { EconomicValue = 10 };
        await arc.AppendAsync(payload, signer);

        Assert.Equal(1, arc.LedgerEntryCount);
    }

    [Fact]
    public async Task VerifyIntegrity_AfterAppend_ReturnsTrue()
    {
        using var arc = ArcFile.CreateInMemory("test.arc");
        using var signer = new SharcSigner("test-agent");
        var pubKey = await signer.GetPublicKeyAsync();

        var info = new AgentInfo("test-agent", AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };
        arc.RegisterAgent(info);

        for (int i = 0; i < 5; i++)
        {
            var payload = new TrustPayload(PayloadType.Text, $"data-{i}") { EconomicValue = i };
            await arc.AppendAsync(payload, signer);
        }

        Assert.True(arc.VerifyIntegrity());
    }

    [Fact]
    public async Task ExportBytes_RoundTrips_ViaFromBytes()
    {
        // Create an arc with data
        byte[] exported;
        using (var arc = ArcFile.CreateInMemory("roundtrip.arc"))
        {
            using var signer = new SharcSigner("agent-1");
            var pubKey = await signer.GetPublicKeyAsync();

            var info = new AgentInfo("agent-1", AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
            var buffer = AgentRegistry.GetVerificationBuffer(info);
            info = info with { Signature = await signer.SignAsync(buffer) };
            arc.RegisterAgent(info);

            var payload = new TrustPayload(PayloadType.Text, "persistent-data") { EconomicValue = 42 };
            await arc.AppendAsync(payload, signer);

            exported = arc.ExportBytes();
        }

        // Restore from bytes
        Assert.True(exported.Length > 0);
        using var restored = ArcFile.FromBytes("roundtrip.arc", exported);

        Assert.True(restored.IsOpfsBacked);
        Assert.True(restored.VerifyIntegrity());
    }

    [Fact]
    public async Task MultipleAppends_AllCountedCorrectly()
    {
        using var arc = ArcFile.CreateInMemory("count.arc");
        using var signer = new SharcSigner("counter-agent");
        var pubKey = await signer.GetPublicKeyAsync();

        var info = new AgentInfo("counter-agent", AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };
        arc.RegisterAgent(info);

        for (int i = 0; i < 10; i++)
        {
            var payload = new TrustPayload(PayloadType.Text, $"entry-{i}");
            await arc.AppendAsync(payload, signer);
        }

        Assert.Equal(10, arc.LedgerEntryCount);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var arc = ArcFile.CreateInMemory("disposable.arc");
        arc.Dispose();
        // No exception = pass
    }
}
