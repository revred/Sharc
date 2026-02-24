// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Mcp;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Mcp;

[Collection("MCP")]
public sealed class RepoContextToolTests : IDisposable
{
    private readonly string _wsPath;
    private readonly string? _savedEnv;

    public RepoContextToolTests()
    {
        _wsPath = Path.Combine(Path.GetTempPath(), $"sharc_mcp_ctx_{Guid.NewGuid()}.arc");
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
    public void AddNote_ReturnsConfirmation()
    {
        var result = RepoContextTool.AddNote("Test note");
        Assert.Equal("Note added.", result);
    }

    [Fact]
    public void ListNotes_AfterAdd_ReturnsNote()
    {
        RepoContextTool.AddNote("Hello world", tag: "test");
        var result = RepoContextTool.ListNotes();
        Assert.Contains("Hello world", result);
        Assert.Contains("test", result);
    }

    [Fact]
    public void ListNotes_Empty_ReturnsNoNotes()
    {
        var result = RepoContextTool.ListNotes();
        Assert.Equal("No notes.", result);
    }

    [Fact]
    public void SetContext_ReturnsConfirmation()
    {
        var result = RepoContextTool.SetContext("project", "Sharc");
        Assert.Contains("project", result);
        Assert.Contains("Sharc", result);
    }

    [Fact]
    public void GetContext_AfterSet_ReturnsValue()
    {
        RepoContextTool.SetContext("project", "Sharc");
        var result = RepoContextTool.GetContext("project");
        Assert.Equal("Sharc", result);
    }

    [Fact]
    public void GetContext_Missing_ReturnsNotFound()
    {
        var result = RepoContextTool.GetContext("nonexistent");
        Assert.Contains("not found", result);
    }

    [Fact]
    public void ListContext_AfterSet_ReturnsEntries()
    {
        RepoContextTool.SetContext("a", "1");
        RepoContextTool.SetContext("b", "2");
        var result = RepoContextTool.ListContext();
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }
}
