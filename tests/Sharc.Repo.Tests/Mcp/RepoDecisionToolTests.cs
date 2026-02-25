// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Mcp;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Mcp;

[Collection("MCP")]
public sealed class RepoDecisionToolTests : IDisposable
{
    private readonly string _wsPath;
    private readonly string? _savedEnv;

    public RepoDecisionToolTests()
    {
        _wsPath = Path.Combine(Path.GetTempPath(), $"sharc_mcp_dec_{Guid.NewGuid()}.arc");
        using var db = WorkspaceSchemaBuilder.CreateSchema(_wsPath);

        _savedEnv = Environment.GetEnvironmentVariable("SHARC_WORKSPACE");
        Environment.SetEnvironmentVariable("SHARC_WORKSPACE", _wsPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SHARC_WORKSPACE", _savedEnv);
        try { File.Delete(_wsPath); } catch { }
        try { File.Delete(_wsPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RecordDecision_ReturnsConfirmation()
    {
        var result = RepoDecisionTool.RecordDecision("ADR-001", "Use CBOR");
        Assert.Contains("ADR-001", result);
    }

    [Fact]
    public void ListDecisions_AfterRecord_ReturnsDecision()
    {
        RepoDecisionTool.RecordDecision("ADR-001", "Use CBOR", rationale: "Compact format");
        var result = RepoDecisionTool.ListDecisions();
        Assert.Contains("ADR-001", result);
        Assert.Contains("Use CBOR", result);
        Assert.Contains("Compact format", result);
    }

    [Fact]
    public void ListDecisions_Empty_ReturnsNoDecisions()
    {
        var result = RepoDecisionTool.ListDecisions();
        Assert.Equal("No decisions.", result);
    }
}
