// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Sharc.Archive;
using Sharc.Archive.Sync;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Archive.Tests.Sync;

public class FragmentSyncTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
            try { File.Delete(f + ".journal"); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath()
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_sync_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        return p;
    }

    private static SharcDatabase CreateArchive(string path)
    {
        return ArchiveSchemaBuilder.CreateSchema(path);
    }

    private static (AgentRegistry registry, LedgerManager ledger, SharcSigner signer) SetupTrust(SharcDatabase db)
    {
        var registry = new AgentRegistry(db);
        var ledger = new LedgerManager(db);
        var signer = new SharcSigner("sync-agent");
        registry.RegisterAgent(CreateTestAgent("sync-agent", signer));
        return (registry, ledger, signer);
    }

    private static AgentInfo CreateTestAgent(string agentId, SharcSigner signer)
    {
        var publicKey = signer.GetPublicKey();
        var validFrom = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds();

        var tempAgent = new AgentInfo(
            agentId, AgentClass.User, publicKey,
            1000, "*", "*", validFrom, 0, "root",
            false, Array.Empty<byte>(), SignatureAlgorithm.HmacSha256);

        var verificationBuffer = AgentRegistry.GetVerificationBuffer(tempAgent);
        var signature = signer.Sign(verificationBuffer);

        return tempAgent with { Signature = signature };
    }

    // ── Preflight ─────────────────────────────────────────────────────

    [Fact]
    public void Preflight_IdenticalEmptyLedgers_InSync()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateArchive(pathL);
        using var dbR = CreateArchive(pathR);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = FragmentSyncProtocol.Preflight(left, right);

        Assert.True(result.InSync);
        Assert.False(result.HasConflict);
    }

    [Fact]
    public void Preflight_RemoteAhead_ReportsRemoteOnly()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateArchive(pathL);
        using var dbR = CreateArchive(pathR);

        // Add ledger entry to remote only
        var (_, ledgerR, signerR) = SetupTrust(dbR);
        ledgerR.Append(new TrustPayload(PayloadType.Text, "remote-entry"), signerR);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = FragmentSyncProtocol.Preflight(left, right);

        Assert.False(result.InSync);
        Assert.False(result.HasConflict);
        Assert.True(result.RemoteOnlyCount > 0);
    }

    // ── Export ─────────────────────────────────────────────────────────

    [Fact]
    public void Export_WithLedgerEntries_ReturnsPayload()
    {
        var path = TempPath();
        using var db = CreateArchive(path);
        var (_, ledger, signer) = SetupTrust(db);
        ledger.Append(new TrustPayload(PayloadType.Text, "entry1"), signer);
        ledger.Append(new TrustPayload(PayloadType.Text, "entry2"), signer);

        var result = FragmentSyncProtocol.Export(db, 1);

        Assert.Equal(2, result.DeltaCount);
        Assert.True(result.Payload.Length > 0);
    }

    [Fact]
    public void Export_FromHighSequence_ReturnsEmpty()
    {
        var path = TempPath();
        using var db = CreateArchive(path);

        var result = FragmentSyncProtocol.Export(db, 999);

        Assert.Equal(0, result.DeltaCount);
    }

    // ── Import ────────────────────────────────────────────────────────

    [Fact]
    public void Import_ValidDeltas_Succeeds()
    {
        // Export from source
        var pathSrc = TempPath();
        using var dbSrc = CreateArchive(pathSrc);
        var (_, ledgerSrc, signerSrc) = SetupTrust(dbSrc);
        ledgerSrc.Append(new TrustPayload(PayloadType.Text, "entry1"), signerSrc);

        var exported = FragmentSyncProtocol.Export(dbSrc, 1);

        // Import into target (must have same agent registered)
        var pathDst = TempPath();
        using var dbDst = CreateArchive(pathDst);
        var registryDst = new AgentRegistry(dbDst);
        registryDst.RegisterAgent(CreateTestAgent("sync-agent", signerSrc));

        var result = FragmentSyncProtocol.Import(dbDst, exported.Payload);

        Assert.True(result.Success);
        Assert.Equal(1, result.ImportedCount);
    }

    // ── SyncFromRemote ────────────────────────────────────────────────

    [Fact]
    public void SyncFromRemote_RemoteAhead_ImportsEntries()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateArchive(pathL);
        using var dbR = CreateArchive(pathR);

        // Both have same agent registered
        var signer = new SharcSigner("sync-agent");
        var agent = CreateTestAgent("sync-agent", signer);

        var registryL = new AgentRegistry(dbL);
        registryL.RegisterAgent(agent);

        var registryR = new AgentRegistry(dbR);
        registryR.RegisterAgent(agent);

        // Remote has extra entries
        var ledgerR = new LedgerManager(dbR);
        ledgerR.Append(new TrustPayload(PayloadType.Text, "remote-1"), signer);
        ledgerR.Append(new TrustPayload(PayloadType.Text, "remote-2"), signer);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = FragmentSyncProtocol.SyncFromRemote(left, right);

        Assert.True(result.Success);
        Assert.Equal(2, result.ImportedCount);
    }

    [Fact]
    public void SyncFromRemote_AlreadyInSync_NoOp()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateArchive(pathL);
        using var dbR = CreateArchive(pathR);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = FragmentSyncProtocol.SyncFromRemote(left, right);

        Assert.True(result.Success);
        Assert.Equal(0, result.ImportedCount);
    }

    [Fact]
    public void SyncFromRemote_WithManifest_UpdatesManifest()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateArchive(pathL);
        using var dbR = CreateArchive(pathR);

        var signer = new SharcSigner("sync-agent");
        var agent = CreateTestAgent("sync-agent", signer);

        new AgentRegistry(dbL).RegisterAgent(agent);
        new AgentRegistry(dbR).RegisterAgent(agent);

        var ledgerR = new LedgerManager(dbR);
        ledgerR.Append(new TrustPayload(PayloadType.Text, "remote-entry"), signer);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        using var mw = new ManifestWriter(dbL);
        var result = FragmentSyncProtocol.SyncFromRemote(left, right, mw);

        Assert.True(result.Success);
        var manifest = mw.FindByFragmentId("right.arc");
        Assert.NotNull(manifest);
        Assert.Equal(1, manifest!.EntryCount);
    }
}
