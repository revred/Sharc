// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class UpdateCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public UpdateCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_update_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        _savedCwd = Directory.GetCurrentDirectory();

        // Initialize a real git repo for update tests
        Directory.SetCurrentDirectory(_tempRoot);
        RunGit("init");
        RunGit("config user.email \"test@test.com\"");
        RunGit("config user.name \"Test User\"");

        // Create initial commit
        File.WriteAllText(Path.Combine(_tempRoot, "README.md"), "# Test");
        RunGit("add .");
        RunGit("commit -m \"Initial commit\"");

        // Initialize .sharc/
        InitCommand.Run(Array.Empty<string>());
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Run_FirstUpdate_IndexesCommits()
    {
        int exitCode = UpdateCommand.Run(Array.Empty<string>());

        Assert.Equal(0, exitCode);

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        Assert.True(reader.CountRows("commits") >= 1);
    }

    [Fact]
    public void Run_FirstUpdate_SetsLastIndexedSha()
    {
        UpdateCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var sha = reader.GetMeta("last_indexed_sha");
        Assert.NotNull(sha);
        Assert.True(sha.Length >= 7);
    }

    [Fact]
    public void Run_IncrementalUpdate_OnlyIndexesNewCommits()
    {
        // First update
        UpdateCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");

        int countAfterFirst;
        using (var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false }))
        {
            countAfterFirst = new WorkspaceReader(db).CountRows("commits");
        }

        // Add a new commit
        File.WriteAllText(Path.Combine(_tempRoot, "file2.txt"), "hello");
        RunGit("add .");
        RunGit("commit -m \"Second commit\"");

        // Second update (incremental)
        UpdateCommand.Run(Array.Empty<string>());

        using (var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false }))
        {
            int countAfterSecond = new WorkspaceReader(db).CountRows("commits");
            Assert.Equal(countAfterFirst + 1, countAfterSecond);
        }
    }

    [Fact]
    public void Run_FullFlag_ReindexesAll()
    {
        // First update
        UpdateCommand.Run(Array.Empty<string>());

        // Full re-index (should not duplicate)
        int exitCode = UpdateCommand.Run(new[] { "--full" });

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_NotInitialized_ReturnsOne()
    {
        var noInitDir = Path.Combine(Path.GetTempPath(), $"sharc_noupdate_{Guid.NewGuid()}");
        Directory.CreateDirectory(noInitDir);
        RunGitIn(noInitDir, "init");

        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noInitDir);
        try
        {
            int exitCode = UpdateCommand.Run(Array.Empty<string>());
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noInitDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_IndexesFileChanges()
    {
        UpdateCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        Assert.True(reader.CountRows("file_changes") >= 1);
    }

    private static void RunGit(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10_000);
    }

    private static void RunGitIn(string workDir, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10_000);
    }
}
