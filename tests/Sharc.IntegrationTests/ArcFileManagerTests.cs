// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.JSInterop;
using Sharc.Arena.Wasm.Services;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class ArcFileManagerTests
{
    /// <summary>
    /// Minimal IJSRuntime stub that reports OPFS as unavailable.
    /// This forces ArcFileManager to operate in pure in-memory mode,
    /// allowing us to test routing, registration, and stats without a browser.
    /// </summary>
    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            // opfsBridge.isSupported → false (no OPFS)
            if (identifier == "opfsBridge.isSupported")
                return new ValueTask<TValue>((TValue)(object)false);
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    private static async Task<ArcFileManager> CreateManagerAsync()
    {
        var manager = new ArcFileManager(new NoOpJsRuntime());
        await manager.InitializeAsync();
        return manager;
    }

    private static async Task<(AgentInfo info, SharcSigner signer)> CreateAgentAsync(string agentId)
    {
        var signer = new SharcSigner(agentId);
        var pubKey = await signer.GetPublicKeyAsync();
        var info = new AgentInfo(agentId, AgentClass.User, pubKey, 100, "*", "*", 0, 0, "", false, Array.Empty<byte>());
        var buffer = AgentRegistry.GetVerificationBuffer(info);
        info = info with { Signature = await signer.SignAsync(buffer) };
        return (info, signer);
    }

    [Fact]
    public async Task InitializeAsync_CreatesThreeArcs()
    {
        await using var manager = await CreateManagerAsync();

        Assert.NotNull(manager.TelemetryArc);
        Assert.NotNull(manager.CriticalArc);
        Assert.NotNull(manager.AgentsArc);
    }

    [Fact]
    public async Task InitializeAsync_WithoutOpfs_GracefulDegradation()
    {
        await using var manager = await CreateManagerAsync();

        Assert.False(manager.OpfsAvailable);
        Assert.False(manager.AgentsRestoredFromOpfs);
        Assert.False(manager.CriticalRestoredFromOpfs);
    }

    [Fact]
    public async Task RoutePayload_LowValue_ReturnsTelemetryArc()
    {
        await using var manager = await CreateManagerAsync();
        var payload = new TrustPayload(PayloadType.Text, "routine-data") { EconomicValue = 1 };

        var target = manager.RoutePayload(payload);

        Assert.Same(manager.TelemetryArc, target);
    }

    [Fact]
    public async Task RoutePayload_HighValue_ReturnsCriticalArc()
    {
        await using var manager = await CreateManagerAsync();
        var payload = new TrustPayload(PayloadType.Text, "warning-data") { EconomicValue = 100 };

        var target = manager.RoutePayload(payload);

        Assert.Same(manager.CriticalArc, target);
    }

    [Fact]
    public async Task RoutePayload_ThresholdValue50_ReturnsCriticalArc()
    {
        await using var manager = await CreateManagerAsync();
        var payload = new TrustPayload(PayloadType.Text, "threshold-data") { EconomicValue = 50 };

        var target = manager.RoutePayload(payload);

        Assert.Same(manager.CriticalArc, target);
    }

    [Fact]
    public async Task RoutePayload_JustBelowThreshold_ReturnsTelemetryArc()
    {
        await using var manager = await CreateManagerAsync();
        var payload = new TrustPayload(PayloadType.Text, "below-threshold") { EconomicValue = 49 };

        var target = manager.RoutePayload(payload);

        Assert.Same(manager.TelemetryArc, target);
    }

    [Fact]
    public async Task RegisterAgentInAllArcs_AgentInAllThree()
    {
        await using var manager = await CreateManagerAsync();
        var (info, _) = await CreateAgentAsync("multi-arc-agent");

        manager.RegisterAgentInAllArcs(info);

        Assert.NotNull(manager.TelemetryArc.Registry.GetAgent("multi-arc-agent"));
        Assert.NotNull(manager.CriticalArc.Registry.GetAgent("multi-arc-agent"));
        Assert.NotNull(manager.AgentsArc.Registry.GetAgent("multi-arc-agent"));
    }

    [Fact]
    public async Task GetStats_ReflectsEntryCountsPerArc()
    {
        await using var manager = await CreateManagerAsync();
        var (info, signer) = await CreateAgentAsync("stats-agent");
        manager.RegisterAgentInAllArcs(info);

        // Append 3 to telemetry (low value)
        for (int i = 0; i < 3; i++)
        {
            var payload = new TrustPayload(PayloadType.Text, $"telem-{i}") { EconomicValue = 1 };
            var target = manager.RoutePayload(payload);
            await target.AppendAsync(payload, signer);
        }

        // Append 2 to critical (high value)
        for (int i = 0; i < 2; i++)
        {
            var payload = new TrustPayload(PayloadType.Text, $"critical-{i}") { EconomicValue = 100 };
            var target = manager.RoutePayload(payload);
            await target.AppendAsync(payload, signer);
        }

        // Append 1 to agents
        var agentPayload = new TrustPayload(PayloadType.System, "AGENT_REGISTRATION:stats-agent", EconomicValue: 1);
        await manager.AgentsArc.AppendAsync(agentPayload, signer);

        var stats = manager.GetStats();
        Assert.Equal(3, stats.TelemetryEntries);
        Assert.Equal(2, stats.CriticalEntries);
        Assert.Equal(1, stats.AgentEntries);
    }

    [Fact]
    public async Task FlushDurableArcsAsync_WithoutOpfs_DoesNotThrow()
    {
        await using var manager = await CreateManagerAsync();

        // Should be a no-op when OPFS is unavailable
        await manager.FlushDurableArcsAsync();
    }

    [Fact]
    public async Task ClearAllOpfsDataAsync_WithoutOpfs_DoesNotThrow()
    {
        await using var manager = await CreateManagerAsync();

        await manager.ClearAllOpfsDataAsync();
    }
}
