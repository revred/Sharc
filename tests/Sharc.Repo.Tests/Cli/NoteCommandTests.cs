// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class NoteCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public NoteCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_note_{Guid.NewGuid()}");
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
    public void Run_BasicNote_ReturnsZero()
    {
        int exitCode = NoteCommand.Run(new[] { "This is a note" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_NoteWithTag_StoresTag()
    {
        NoteCommand.Run(new[] { "Tagged note", "--tag", "bug" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var notes = reader.ReadNotes(tag: "bug");
        Assert.Single(notes);
        Assert.Equal("Tagged note", notes[0].Content);
    }

    [Fact]
    public void Run_NoteWithAuthor_StoresAuthor()
    {
        NoteCommand.Run(new[] { "Author note", "--author", "alice" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var notes = reader.ReadNotes();
        Assert.Single(notes);
        Assert.Equal("alice", notes[0].Author);
    }

    [Fact]
    public void Run_NoContent_ReturnsOne()
    {
        int exitCode = NoteCommand.Run(Array.Empty<string>());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_ChannelDisabled_ReturnsOne()
    {
        // Disable notes channel
        var configPath = Path.Combine(_tempRoot, ".sharc", "config.arc");
        using (var db = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = true }))
        {
            using var cw = new ConfigWriter(db);
            cw.Set("channel.notes", "disabled");
        }

        int exitCode = NoteCommand.Run(new[] { "Should fail" });
        Assert.Equal(1, exitCode);
    }
}
