// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Mcp;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Mcp;

[Collection("MCP")]
public sealed class RepoAnnotateToolTests : IDisposable
{
    private readonly string _wsPath;
    private readonly string? _savedEnv;

    public RepoAnnotateToolTests()
    {
        _wsPath = Path.Combine(Path.GetTempPath(), $"sharc_mcp_ann_{Guid.NewGuid()}.arc");
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
    public void AnnotateFile_ReturnsConfirmation()
    {
        var result = RepoAnnotateTool.AnnotateFile("src/Foo.cs", "Needs refactoring");
        Assert.Contains("src/Foo.cs", result);
    }

    [Fact]
    public void ListAnnotations_AfterAnnotate_ReturnsAnnotation()
    {
        RepoAnnotateTool.AnnotateFile("src/Foo.cs", "Fix bug", type: "bug", lineStart: 10);
        var result = RepoAnnotateTool.ListAnnotations();
        Assert.Contains("src/Foo.cs", result);
        Assert.Contains("bug", result);
        Assert.Contains("Fix bug", result);
    }

    [Fact]
    public void ListAnnotations_Empty_ReturnsNoAnnotations()
    {
        var result = RepoAnnotateTool.ListAnnotations();
        Assert.Equal("No annotations.", result);
    }
}
