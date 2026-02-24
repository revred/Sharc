// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class SetGetCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public SetGetCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_setget_{Guid.NewGuid()}");
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

    // ── SetCommand ───────────────────────────────────────────────────

    [Fact]
    public void Set_NewKey_ReturnsZero()
    {
        int exitCode = SetCommand.Run(new[] { "project.name", "Sharc" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Set_NewKey_StoresValue()
    {
        SetCommand.Run(new[] { "project.name", "Sharc" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var entries = reader.ReadContext(key: "project.name");
        Assert.Single(entries);
        Assert.Equal("Sharc", entries[0].Value);
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValue()
    {
        SetCommand.Run(new[] { "project.name", "Sharc" });
        SetCommand.Run(new[] { "project.name", "Sharc2" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var entries = reader.ReadContext(key: "project.name");
        Assert.Single(entries);
        Assert.Equal("Sharc2", entries[0].Value);
    }

    [Fact]
    public void Set_MissingArgs_ReturnsOne()
    {
        int exitCode = SetCommand.Run(new[] { "project.name" });
        Assert.Equal(1, exitCode);
    }

    // ── GetCommand ───────────────────────────────────────────────────

    [Fact]
    public void Get_ExistingKey_ReturnsZero()
    {
        SetCommand.Run(new[] { "project.name", "Sharc" });

        int exitCode = GetCommand.Run(new[] { "project.name" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Get_MissingKey_ReturnsOne()
    {
        int exitCode = GetCommand.Run(new[] { "nonexistent.key" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Get_AllFlag_ReturnsZero()
    {
        SetCommand.Run(new[] { "a.key", "val1" });
        SetCommand.Run(new[] { "b.key", "val2" });

        int exitCode = GetCommand.Run(new[] { "--all" });
        Assert.Equal(0, exitCode);
    }
}
