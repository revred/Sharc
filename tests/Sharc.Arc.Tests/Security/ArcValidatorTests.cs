// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Sharc.Arc.Security;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Arc.Tests.Security;

public class ArcValidatorTests : IDisposable
{
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

    private string TempPath()
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_val_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        return p;
    }

    private ArcHandle CreateValidArcWithLedger(string agentId = "test-agent")
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        var registry = new AgentRegistry(db);
        var ledger = new LedgerManager(db);

        var signer = new SharcSigner(agentId);
        var agent = CreateTestAgent(agentId, signer);
        registry.RegisterAgent(agent);
        ledger.Append(new TrustPayload(PayloadType.Text, "test entry"), signer);

        return new ArcHandle(Path.GetFileName(path), db, ArcUri.FromLocalPath(path));
    }

    private ArcHandle CreateEmptyArc()
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        return new ArcHandle(Path.GetFileName(path), db, ArcUri.FromLocalPath(path));
    }

    // ── Format validation (Layer 1) ──────────────────────────────────

    [Fact]
    public void PreValidate_ValidSqliteHeader_ReturnsTrue()
    {
        // "SQLite format 3\0" magic header (16 bytes)
        byte[] header = "SQLite format 3\0"u8.ToArray();

        var result = ArcValidator.PreValidate(header);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PreValidate_InvalidMagic_ReturnsInvalid()
    {
        byte[] header = new byte[16]; // all zeros

        var result = ArcValidator.PreValidate(header);

        Assert.False(result.IsValid);
        Assert.Contains("magic", result.Violations[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreValidate_TruncatedHeader_ReturnsInvalid()
    {
        byte[] header = new byte[] { 0x53, 0x51, 0x4C }; // "SQL" only

        var result = ArcValidator.PreValidate(header);

        Assert.False(result.IsValid);
        Assert.Contains("too small", result.Violations[0], StringComparison.OrdinalIgnoreCase);
    }

    // ── Chain integrity (Layer 2) ────────────────────────────────────

    [Fact]
    public void Validate_ValidArcWithLedger_ChainIntact()
    {
        using var handle = CreateValidArcWithLedger();

        var report = ArcValidator.Validate(handle);

        Assert.True(report.IsValid);
        Assert.True(report.ChainIntact);
        Assert.Equal(1, report.LedgerEntryCount);
    }

    [Fact]
    public void Validate_EmptyArc_NoLedger_StillValid()
    {
        using var handle = CreateEmptyArc();

        var report = ArcValidator.Validate(handle);

        Assert.True(report.IsValid);
        Assert.True(report.ChainIntact);
        Assert.Equal(0, report.LedgerEntryCount);
    }

    // ── Trust anchor checking (Layer 3) ──────────────────────────────

    [Fact]
    public void Validate_UnknownSigner_RejectPolicy_NotValid()
    {
        using var handle = CreateValidArcWithLedger("untrusted-agent");

        var report = ArcValidator.Validate(handle, new ArcValidationOptions
        {
            TrustAnchors = TrustAnchorSet.Empty,
            UnknownSignerPolicy = TrustPolicy.RejectUnknown
        });

        Assert.False(report.IsValid);
        Assert.False(report.AllSignersTrusted);
        Assert.Contains("untrusted-agent", report.UnknownSigners);
    }

    [Fact]
    public void Validate_UnknownSigner_WarnPolicy_ValidWithWarnings()
    {
        using var handle = CreateValidArcWithLedger("warn-agent");

        var report = ArcValidator.Validate(handle, new ArcValidationOptions
        {
            TrustAnchors = TrustAnchorSet.Empty,
            UnknownSignerPolicy = TrustPolicy.WarnUnknown
        });

        Assert.True(report.IsValid);
        Assert.False(report.AllSignersTrusted);
        Assert.Contains("warn-agent", report.UnknownSigners);
        Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public void Validate_KnownSigner_NoWarnings()
    {
        using var handle = CreateValidArcWithLedger("known-agent");

        // Build trust anchors from the same arc (self-trust)
        var anchors = TrustAnchorSet.FromArc(handle);

        var report = ArcValidator.Validate(handle, new ArcValidationOptions
        {
            TrustAnchors = anchors,
            UnknownSignerPolicy = TrustPolicy.RejectUnknown
        });

        Assert.True(report.IsValid);
        Assert.True(report.AllSignersTrusted);
        Assert.Empty(report.UnknownSigners);
        Assert.Empty(report.Warnings);
    }

    // ── Payload limits (Layer 4) ─────────────────────────────────────

    [Fact]
    public void Validate_ExceedsMaxEntryCount_ReportsViolation()
    {
        using var handle = CreateValidArcWithLedger();

        var report = ArcValidator.Validate(handle, new ArcValidationOptions
        {
            MaxLedgerEntryCount = 0 // no entries allowed
        });

        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("entry count", StringComparison.OrdinalIgnoreCase));
    }

    // ── Helper ────────────────────────────────────────────────────────

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
}
