// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Sharc.Arc.Locators;
using Sharc.Arc.Security;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Arc.Tests.Locators;

public class LocalArcLocatorTests : IDisposable
{
    private readonly LocalArcLocator _locator = new();
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f)) File.Delete(f);
            if (File.Exists(f + ".journal")) File.Delete(f + ".journal");
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath(string ext = ".arc")
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_test_{Guid.NewGuid()}{ext}");
        _tempFiles.Add(p);
        return p;
    }

    private string CreateValidArc()
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        db.Dispose();
        return path;
    }

    // ── Happy path ──────────────────────────────────────────────────

    [Fact]
    public void TryOpen_ExistingFile_ReturnsAvailable()
    {
        var path = CreateValidArc();
        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions { ValidateOnOpen = false });

        Assert.True(result.IsAvailable);
        Assert.Equal(ArcAvailability.Available, result.Availability);
        Assert.NotNull(result.Handle);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TryOpen_ValidArcWithLedger_IntegrityPasses()
    {
        var path = CreateValidArc();
        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions { ValidateOnOpen = true });

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public void TryOpen_ValidateOnOpenFalse_SkipsIntegrity()
    {
        var path = CreateValidArc();
        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions { ValidateOnOpen = false });

        Assert.True(result.IsAvailable);
    }

    // ── Threat: Missing/unreachable file ────────────────────────────

    [Fact]
    public void TryOpen_MissingFile_ReturnsUnreachable()
    {
        var uri = ArcUri.FromLocalPath("/nonexistent/path/missing.arc");

        using var result = _locator.TryOpen(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Unreachable, result.Availability);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    // ── Threat: Crafted file mimicking arc format ───────────────────

    [Fact]
    public void TryOpen_InvalidMagic_ReturnsUntrusted()
    {
        var path = TempPath();
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05,
            0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 });
        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Untrusted, result.Availability);
        Assert.Contains("magic", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryOpen_TruncatedFile_ReturnsUntrusted()
    {
        var path = TempPath();
        File.WriteAllBytes(path, new byte[] { 0x01, 0x02, 0x03 }); // too small
        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Untrusted, result.Availability);
        Assert.Contains("too small", result.ErrorMessage!);
    }

    // ── Threat: Oversized file (DoS via resource exhaustion) ────────

    [Fact]
    public void TryOpen_OversizedFile_ReturnsUntrusted()
    {
        var path = CreateValidArc();
        var uri = ArcUri.FromLocalPath(path);

        // Set absurdly low limit
        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            MaxFileSizeBytes = 1, // 1 byte limit
            ValidateOnOpen = false
        });

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Untrusted, result.Availability);
        Assert.Contains("size limit", result.ErrorMessage!);
    }

    // ── Threat: Path traversal ──────────────────────────────────────

    [Fact]
    public void TryOpen_PathTraversal_ReturnsUntrusted()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"sharc_base_{Guid.NewGuid()}");
        Directory.CreateDirectory(baseDir);
        _tempFiles.Add(baseDir); // cleanup

        var uri = ArcUri.FromLocalPath("../../etc/passwd");

        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            BaseDirectory = baseDir,
            ValidateOnOpen = false
        });

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Untrusted, result.Availability);
        Assert.Contains("traversal", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
    }

    [Fact]
    public void TryOpen_RelativePath_WithBaseDirectory_ResolvesCorrectly()
    {
        var baseDir = Path.GetTempPath();
        var arcPath = CreateValidArc();
        var relativeName = Path.GetFileName(arcPath);
        var uri = ArcUri.FromLocalPath(relativeName);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            BaseDirectory = baseDir,
            ValidateOnOpen = false
        });

        Assert.True(result.IsAvailable);
    }

    // ── Threat: Unknown signers (trust anchor checking) ─────────────

    [Fact]
    public void TryOpen_UnknownSigner_RejectPolicy_ReturnsUntrusted()
    {
        // Create an arc with a registered agent + ledger entry
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        var registry = new AgentRegistry(db);
        var ledger = new LedgerManager(db);

        var signer = new SharcSigner("test-agent-1");
        var agent = CreateTestAgent("test-agent-1", signer);
        registry.RegisterAgent(agent);
        ledger.Append(new TrustPayload(PayloadType.Text, "test"), signer);
        db.Dispose();

        // Open with trust anchors that do NOT include this agent
        var uri = ArcUri.FromLocalPath(path);
        var emptyAnchors = TrustAnchorSet.Empty;

        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            ValidateOnOpen = true,
            TrustAnchors = emptyAnchors,
            UnknownSignerPolicy = TrustPolicy.RejectUnknown
        });

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Untrusted, result.Availability);
        Assert.Contains("test-agent-1", result.ErrorMessage!);
    }

    [Fact]
    public void TryOpen_UnknownSigner_WarnPolicy_ReturnsAvailableWithWarnings()
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        var registry = new AgentRegistry(db);
        var ledger = new LedgerManager(db);

        var signer = new SharcSigner("agent-warn");
        var agent = CreateTestAgent("agent-warn", signer);
        registry.RegisterAgent(agent);
        ledger.Append(new TrustPayload(PayloadType.Text, "test"), signer);
        db.Dispose();

        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            ValidateOnOpen = true,
            TrustAnchors = TrustAnchorSet.Empty,
            UnknownSignerPolicy = TrustPolicy.WarnUnknown
        });

        Assert.True(result.IsAvailable);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("agent-warn"));
    }

    [Fact]
    public void TryOpen_KnownSigner_AcceptAll_NoWarnings()
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        var registry = new AgentRegistry(db);
        var ledger = new LedgerManager(db);

        var signer = new SharcSigner("trusted-agent");
        var agent = CreateTestAgent("trusted-agent", signer);
        registry.RegisterAgent(agent);
        ledger.Append(new TrustPayload(PayloadType.Text, "test"), signer);
        db.Dispose();

        var uri = ArcUri.FromLocalPath(path);

        using var result = _locator.TryOpen(uri, new ArcOpenOptions
        {
            ValidateOnOpen = true,
            TrustAnchors = TrustAnchorSet.Empty,
            UnknownSignerPolicy = TrustPolicy.AcceptAll
        });

        Assert.True(result.IsAvailable);
        Assert.Empty(result.Warnings);
    }

    // ── Threat: Unsupported authority ───────────────────────────────

    [Fact]
    public void TryOpen_WrongAuthority_StillWorksLocator()
    {
        // LocalArcLocator doesn't check authority (ArcResolver does),
        // but let's verify it doesn't crash
        var path = CreateValidArc();
        var uri = ArcUri.Parse($"arc://local/{path.Replace('\\', '/')}");

        using var result = _locator.TryOpen(uri, new ArcOpenOptions { ValidateOnOpen = false });

        // Should succeed since the path resolves
        Assert.True(result.IsAvailable);
    }

    // ── Helper ──────────────────────────────────────────────────────

    private static AgentInfo CreateTestAgent(string agentId, SharcSigner signer)
    {
        var publicKey = signer.GetPublicKey();
        // ValidityStart is compared in seconds (timestamp / 1_000_000)
        // Set to 1 second in the past to ensure validity window is open
        var validFrom = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds();

        var tempAgent = new AgentInfo(
            agentId, AgentClass.User, publicKey,
            1000, "*", "*", validFrom, 0, "root",
            false, Array.Empty<byte>(), SignatureAlgorithm.HmacSha256);

        var verificationBuffer = AgentRegistry.GetVerificationBuffer(tempAgent);
        var signature = signer.Sign(verificationBuffer);

        return tempAgent with { Signature = signature };
    }
}
