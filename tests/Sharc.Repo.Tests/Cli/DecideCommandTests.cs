// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class DecideCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public DecideCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_decide_{Guid.NewGuid()}");
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
    public void Run_BasicDecision_ReturnsZero()
    {
        int exitCode = DecideCommand.Run(new[] { "ADR-001", "Use SharcWriter for all writes" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_WithRationale_StoresRationale()
    {
        DecideCommand.Run(new[] { "ADR-002", "Use CBOR for metadata",
            "--rationale", "Compact binary format" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var decisions = reader.ReadDecisions();
        Assert.Single(decisions);
        Assert.Equal("ADR-002", decisions[0].DecisionId);
        Assert.Equal("Compact binary format", decisions[0].Rationale);
    }

    [Fact]
    public void Run_WithStatus_StoresStatus()
    {
        DecideCommand.Run(new[] { "ADR-003", "Evaluate DuckDB",
            "--status", "proposed" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var decisions = reader.ReadDecisions(status: "proposed");
        Assert.Single(decisions);
    }

    [Fact]
    public void Run_DefaultStatus_IsAccepted()
    {
        DecideCommand.Run(new[] { "ADR-004", "Default status test" });

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);
        var decisions = reader.ReadDecisions();
        Assert.Single(decisions);
        Assert.Equal("accepted", decisions[0].Status);
    }

    [Fact]
    public void Run_MissingArgs_ReturnsOne()
    {
        int exitCode = DecideCommand.Run(new[] { "ADR-001" });
        Assert.Equal(1, exitCode);
    }
}
