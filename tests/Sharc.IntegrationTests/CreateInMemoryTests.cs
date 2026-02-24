// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class CreateInMemoryTests : IDisposable
{
    private SharcDatabase? _db;

    public void Dispose()
    {
        _db?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateInMemory_ReturnsWritableDatabase()
    {
        _db = SharcDatabase.CreateInMemory();

        Assert.NotNull(_db);
        var schema = _db.Schema;
        var tableNames = schema.Tables.Select(t => t.Name).ToList();

        Assert.Contains("_sharc_ledger", tableNames);
        Assert.Contains("_sharc_agents", tableNames);
        Assert.Contains("_sharc_scores", tableNames);
        Assert.Contains("_sharc_audit", tableNames);
    }

    [Fact]
    public async Task CreateInMemory_CanRegisterAndAppend()
    {
        _db = SharcDatabase.CreateInMemory();

        var registry = new AgentRegistry(_db);
        var ledger = new LedgerManager(_db);

        // Create a signer and register an agent
        using var signer = new SharcSigner("test-sensor-1");
        var info = new AgentInfo(
            AgentId: signer.AgentId,
            Class: AgentClass.User,
            PublicKey: await signer.GetPublicKeyAsync(),
            AuthorityCeiling: 100,
            WriteScope: "*",
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: Array.Empty<byte>());

        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };
        registry.RegisterAgent(info);

        // Append a payload to the ledger
        var payload = new TrustPayload(PayloadType.Text, "test-telemetry-data")
        {
            EconomicValue = 1
        };

        await ledger.AppendAsync(payload, signer);

        // Verify the chain integrity
        Assert.True(ledger.VerifyIntegrity());
    }
}
