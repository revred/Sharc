// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.JSInterop;
using Sharc.Core.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Manages multiple .arc files for the flight simulator per the Arc Format specification.
/// Routes payloads to the appropriate arc based on economic value, handles OPFS persistence
/// for durable arcs, and provides a unified query interface.
/// <para>
/// Arc file layout (per docs/TheArcFormat.md Section 4 — Sensor Sharding):
/// <list type="bullet">
///   <item><c>telemetry.arc</c> — volatile, in-memory: high-frequency raw sensor readings</item>
///   <item><c>critical_events.arc</c> — durable, OPFS: warnings and threshold-crossing events</item>
///   <item><c>agents.arc</c> — durable, OPFS: agent registrations and identity continuity</item>
/// </list>
/// </para>
/// </summary>
public sealed class ArcFileManager : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private bool _opfsAvailable;

    private const string CriticalArcFileName = "critical_events.arc";
    private const string AgentsArcFileName = "agents.arc";

    private long _lastCriticalFlushVersion;
    private long _lastAgentsFlushVersion;

    /// <summary>Volatile arc for high-frequency telemetry.</summary>
    public ArcFile TelemetryArc { get; private set; } = null!;

    /// <summary>Durable arc for critical events (OPFS-backed).</summary>
    public ArcFile CriticalArc { get; private set; } = null!;

    /// <summary>Durable arc for agent identity (OPFS-backed).</summary>
    public ArcFile AgentsArc { get; private set; } = null!;

    /// <summary>True if agent identity was restored from a prior OPFS session.</summary>
    public bool AgentsRestoredFromOpfs { get; private set; }

    /// <summary>True if critical events were restored from a prior OPFS session.</summary>
    public bool CriticalRestoredFromOpfs { get; private set; }

    /// <summary>Whether OPFS is available in this browser.</summary>
    public bool OpfsAvailable => _opfsAvailable;

    public ArcFileManager(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Initializes all arc files. Attempts to restore durable arcs from OPFS.
    /// Falls back to in-memory if OPFS is unavailable or data is corrupt.
    /// </summary>
    public async Task InitializeAsync()
    {
        _opfsAvailable = await CheckOpfsAvailableAsync();

        // 1. Telemetry: always volatile in-memory
        TelemetryArc = ArcFile.CreateInMemory("telemetry.arc");

        // 2. Critical events: try OPFS restore
        CriticalArc = await TryRestoreOrCreateAsync(CriticalArcFileName, "critical_events.arc");
        CriticalRestoredFromOpfs = CriticalArc.IsOpfsBacked;

        // 3. Agents: try OPFS restore
        AgentsArc = await TryRestoreOrCreateAsync(AgentsArcFileName, "agents.arc");
        AgentsRestoredFromOpfs = AgentsArc.IsOpfsBacked;
    }

    /// <summary>
    /// Registers an agent across all three arc files.
    /// Each arc's LedgerManager validates agents from its own internal AgentRegistry,
    /// so agents must be present in every arc that will receive their payloads.
    /// </summary>
    public void RegisterAgentInAllArcs(AgentInfo agent)
    {
        TelemetryArc.RegisterAgent(agent);
        CriticalArc.RegisterAgent(agent);
        AgentsArc.RegisterAgent(agent);
    }

    /// <summary>
    /// Routes a payload to the appropriate arc file based on economic value.
    /// Critical payloads (EconomicValue >= 50) go to the durable CriticalArc.
    /// Routine telemetry goes to the volatile TelemetryArc.
    /// </summary>
    public ArcFile RoutePayload(TrustPayload payload)
    {
        return payload.EconomicValue >= 50 ? CriticalArc : TelemetryArc;
    }

    /// <summary>
    /// Flushes durable arcs to OPFS if their data has changed since last flush.
    /// Only writes when the page source DataVersion has advanced.
    /// </summary>
    public async Task FlushDurableArcsAsync()
    {
        if (!_opfsAvailable) return;

        // Use LedgerEntryCount as a change indicator since the PageSource is not
        // always IWritablePageSource (e.g., ProxyPageSource wraps the real source).
        long criticalVersion = CriticalArc.LedgerEntryCount;
        if (criticalVersion != _lastCriticalFlushVersion)
        {
            await PersistToOpfsAsync(CriticalArc, CriticalArcFileName);
            _lastCriticalFlushVersion = criticalVersion;
        }

        long agentsVersion = AgentsArc.LedgerEntryCount;
        if (agentsVersion != _lastAgentsFlushVersion)
        {
            await PersistToOpfsAsync(AgentsArc, AgentsArcFileName);
            _lastAgentsFlushVersion = agentsVersion;
        }
    }

    /// <summary>
    /// Deletes all OPFS arc files for a fresh start.
    /// </summary>
    public async Task ClearAllOpfsDataAsync()
    {
        if (!_opfsAvailable) return;

        try { await _js.InvokeAsync<bool>("opfsBridge.deleteDatabase", CriticalArcFileName); } catch { }
        try { await _js.InvokeAsync<bool>("opfsBridge.deleteDatabase", AgentsArcFileName); } catch { }
    }

    /// <summary>
    /// Gets aggregate statistics across all arcs.
    /// </summary>
    public (int TelemetryEntries, int CriticalEntries, int AgentEntries) GetStats()
    {
        return (
            TelemetryArc.LedgerEntryCount,
            CriticalArc.LedgerEntryCount,
            AgentsArc.LedgerEntryCount
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { await FlushDurableArcsAsync(); } catch { }
        TelemetryArc?.Dispose();
        CriticalArc?.Dispose();
        AgentsArc?.Dispose();
    }

    // ── Private helpers ──

    private async Task<bool> CheckOpfsAvailableAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("opfsBridge.isSupported");
        }
        catch
        {
            return false;
        }
    }

    private async Task<ArcFile> TryRestoreOrCreateAsync(string opfsFileName, string arcName)
    {
        if (!_opfsAvailable)
            return ArcFile.CreateInMemory(arcName, durable: true);

        try
        {
            var files = await _js.InvokeAsync<string[]>("opfsBridge.listDatabases");
            if (files.Contains(opfsFileName))
            {
                var opfs = await OpfsPageSource.OpenAsync(_js, opfsFileName);
                var bytes = ReadAllPagesFromOpfs(opfs);
                opfs.Dispose();

                if (bytes.Length > 0)
                {
                    var arc = ArcFile.FromBytes(arcName, bytes);
                    if (arc.VerifyIntegrity())
                        return arc;

                    // Integrity check failed — discard corrupt data
                    arc.Dispose();
                }
            }
        }
        catch
        {
            // OPFS read failed — fall through to create fresh
        }

        return ArcFile.CreateInMemory(arcName, durable: true);
    }

    private static byte[] ReadAllPagesFromOpfs(OpfsPageSource opfs)
    {
        int pageSize = opfs.PageSize;
        int pageCount = opfs.PageCount;
        if (pageCount == 0) return Array.Empty<byte>();

        var bytes = new byte[pageSize * pageCount];
        for (int i = 1; i <= pageCount; i++)
            opfs.ReadPage((uint)i, bytes.AsSpan((i - 1) * pageSize, pageSize));

        return bytes;
    }

    private async Task PersistToOpfsAsync(ArcFile arc, string fileName)
    {
        var bytes = arc.ExportBytes();
        try { await _js.InvokeAsync<bool>("opfsBridge.deleteDatabase", fileName); } catch { }
        var opfs = await OpfsPageSource.CreateFromBytesAsync(_js, fileName, bytes);
        opfs.Dispose();
    }
}
