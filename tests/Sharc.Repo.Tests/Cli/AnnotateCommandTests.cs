// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class AnnotateCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public AnnotateCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_annot_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
        InitCommand.Run(Array.Empty<string>());
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Run_BasicAnnotation_ReturnsZero()
    {
        int exitCode = AnnotateCommand.Run(new[] { "src/Foo.cs", "Needs refactoring" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_WithType_StoresAnnotationType()
    {
        AnnotateCommand.Run(new[] { "src/Foo.cs", "Fix this", "--type", "bug" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var annotations = reader.ReadFileAnnotations(type: "bug");
        Assert.Single(annotations);
        Assert.Equal("src/Foo.cs", annotations[0].FilePath);
    }

    [Fact]
    public void Run_WithLineRange_StoresLines()
    {
        AnnotateCommand.Run(new[] { "src/Foo.cs", "Check logic", "--line", "10-20" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var annotations = reader.ReadFileAnnotations();
        Assert.Single(annotations);
        Assert.Equal(10, annotations[0].LineStart);
        Assert.Equal(20, annotations[0].LineEnd);
    }

    [Fact]
    public void Run_WithSingleLine_StoresSameStartEnd()
    {
        AnnotateCommand.Run(new[] { "src/Foo.cs", "Check this line", "--line", "42" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var annotations = reader.ReadFileAnnotations();
        Assert.Single(annotations);
        Assert.Equal(42, annotations[0].LineStart);
        Assert.Equal(42, annotations[0].LineEnd);
    }

    [Fact]
    public void Run_MissingArgs_ReturnsOne()
    {
        int exitCode = AnnotateCommand.Run(new[] { "src/Foo.cs" });
        Assert.Equal(1, exitCode);
    }
}
