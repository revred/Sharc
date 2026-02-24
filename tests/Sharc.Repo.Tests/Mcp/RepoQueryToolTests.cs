// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Mcp;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Mcp;

[Collection("MCP")]
public sealed class RepoQueryToolTests : IDisposable
{
    private readonly string _wsPath;
    private readonly string? _savedEnv;

    public RepoQueryToolTests()
    {
        _wsPath = Path.Combine(Path.GetTempPath(), $"sharc_mcp_qry_{Guid.NewGuid()}.arc");
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
    public void QueryWorkspace_EmptyTable_ReturnsNoRows()
    {
        var result = RepoQueryTool.QueryWorkspace("notes");
        Assert.Contains("no rows", result);
    }

    [Fact]
    public void QueryWorkspace_WithData_ReturnsRows()
    {
        RepoContextTool.AddNote("Test note");
        var result = RepoQueryTool.QueryWorkspace("notes");
        Assert.Contains("Test note", result);
    }

    [Fact]
    public void QueryWorkspace_InvalidTable_ReturnsError()
    {
        var result = RepoQueryTool.QueryWorkspace("nonexistent");
        Assert.Contains("Error", result);
    }

    [Fact]
    public void GetStatus_ReturnsMarkdownReport()
    {
        var result = RepoQueryTool.GetStatus();
        Assert.Contains("Workspace Status", result);
        Assert.Contains("notes", result);
        Assert.Contains("commits", result);
    }
}
