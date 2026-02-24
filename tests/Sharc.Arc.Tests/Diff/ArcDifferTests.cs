// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Arc;
using Sharc.Arc.Diff;
using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Arc.Tests.Diff;

public class ArcDifferTests : IDisposable
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
        var p = Path.Combine(Path.GetTempPath(), $"sharc_diff_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        return p;
    }

    private static SharcDatabase CreateDbWithTable(string path, string tableName,
        string createSql, Action<SharcDatabase>? populate = null)
    {
        var db = SharcDatabase.Create(path);
        using (var tx = db.BeginTransaction())
        {
            tx.Execute(createSql);
            tx.Commit();
        }
        populate?.Invoke(db);
        return db;
    }

    private static void InsertRow(SharcDatabase db, string table, long id, string val)
    {
        using var writer = SharcWriter.From(db);
        writer.Insert(table,
            ColumnValue.FromInt64(1, id),
            ColumnValue.Text(val.Length, Encoding.UTF8.GetBytes(val)));
    }

    private static void ExecuteDdl(SharcDatabase db, string sql)
    {
        using var tx = db.BeginTransaction();
        tx.Execute(sql);
        tx.Commit();
    }

    // ── Schema Diff ──────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalSchemas_ReportsIdentical()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        using var dbR = CreateDbWithTable(pathR, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Schema });

        Assert.NotNull(result.Schema);
        Assert.True(result.Schema.IsIdentical);
        Assert.Empty(result.Schema.TablesOnlyInLeft);
        Assert.Empty(result.Schema.TablesOnlyInRight);
        Assert.Empty(result.Schema.ModifiedTables);
    }

    [Fact]
    public void Diff_TableAddedInRight_ReportsOnlyInRight()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        using var dbR = CreateDbWithTable(pathR, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)",
            db => ExecuteDdl(db, "CREATE TABLE logs (id INTEGER PRIMARY KEY, msg TEXT)"));

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Schema });

        Assert.False(result.Schema!.IsIdentical);
        Assert.Empty(result.Schema.TablesOnlyInLeft);
        Assert.Contains("logs", result.Schema.TablesOnlyInRight);
    }

    [Fact]
    public void Diff_TableRemovedFromRight_ReportsOnlyInLeft()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)",
            db => ExecuteDdl(db, "CREATE TABLE logs (id INTEGER PRIMARY KEY, msg TEXT)"));
        using var dbR = CreateDbWithTable(pathR, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Schema });

        Assert.False(result.Schema!.IsIdentical);
        Assert.Contains("logs", result.Schema.TablesOnlyInLeft);
        Assert.Empty(result.Schema.TablesOnlyInRight);
    }

    [Fact]
    public void Diff_ColumnTypeChanged_ReportsModifiedTable()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        using var dbR = CreateDbWithTable(pathR, "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name BLOB)");

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Schema });

        Assert.False(result.Schema!.IsIdentical);
        Assert.Single(result.Schema.ModifiedTables);
        var mod = result.Schema.ModifiedTables[0];
        Assert.Equal("users", mod.TableName);
        Assert.Single(mod.TypeChanges);
        Assert.Equal("name", mod.TypeChanges[0].ColumnName);
        Assert.Equal("TEXT", mod.TypeChanges[0].LeftType);
        Assert.Equal("BLOB", mod.TypeChanges[0].RightType);
    }

    // ── Ledger Diff ──────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalLedgers_ReportsIdentical()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = SharcDatabase.Create(pathL);
        using var dbR = SharcDatabase.Create(pathR);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Ledger });

        Assert.NotNull(result.Ledger);
        Assert.True(result.Ledger.IsIdentical);
    }

    [Fact]
    public void Diff_LeftHasExtraLedgerEntries_ReportsLeftOnly()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = SharcDatabase.Create(pathL);
        using var dbR = SharcDatabase.Create(pathR);

        var registryL = new AgentRegistry(dbL);
        var ledgerL = new LedgerManager(dbL);
        var signerL = new SharcSigner("agent-1");
        registryL.RegisterAgent(CreateTestAgent("agent-1", signerL));
        ledgerL.Append(new TrustPayload(PayloadType.Text, "entry1"), signerL);

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Ledger });

        Assert.False(result.Ledger!.IsIdentical);
        Assert.Equal(1, result.Ledger.LeftTotalCount);
        Assert.Equal(0, result.Ledger.RightTotalCount);
        Assert.Equal(1, result.Ledger.LeftOnlyCount);
    }

    // ── Data Diff ────────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalData_ReportsIdentical()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "beta"); });
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "beta"); });

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Data });

        Assert.Single(result.Tables);
        Assert.True(result.Tables[0].IsIdentical);
        Assert.Equal(2, result.Tables[0].MatchingRowCount);
        Assert.Equal(0, result.Tables[0].ModifiedRowCount);
    }

    [Fact]
    public void Diff_ModifiedRow_ReportsModified()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "beta"); });
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "CHANGED"); });

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Data });

        Assert.Single(result.Tables);
        Assert.False(result.Tables[0].IsIdentical);
        Assert.Equal(1, result.Tables[0].MatchingRowCount);
        Assert.Equal(1, result.Tables[0].ModifiedRowCount);
    }

    [Fact]
    public void Diff_InsertedRow_ReportsRightOnly()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => InsertRow(db, "items", 1, "alpha"));
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "beta"); });

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Data });

        Assert.Single(result.Tables);
        Assert.False(result.Tables[0].IsIdentical);
        Assert.Equal(1, result.Tables[0].MatchingRowCount);
        Assert.Equal(1, result.Tables[0].RightOnlyRowCount);
        Assert.Equal(0, result.Tables[0].LeftOnlyRowCount);
    }

    [Fact]
    public void Diff_DeletedRow_ReportsLeftOnly()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => { InsertRow(db, "items", 1, "alpha"); InsertRow(db, "items", 2, "beta"); });
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => InsertRow(db, "items", 1, "alpha"));

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Data });

        Assert.Single(result.Tables);
        Assert.False(result.Tables[0].IsIdentical);
        Assert.Equal(1, result.Tables[0].MatchingRowCount);
        Assert.Equal(1, result.Tables[0].LeftOnlyRowCount);
        Assert.Equal(0, result.Tables[0].RightOnlyRowCount);
    }

    [Fact]
    public void Diff_EmptyTables_ReportsIdentical()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)");
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)");

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions { Scope = DiffScope.Data });

        Assert.Single(result.Tables);
        Assert.True(result.Tables[0].IsIdentical);
        Assert.Equal(0, result.Tables[0].LeftRowCount);
        Assert.Equal(0, result.Tables[0].RightRowCount);
    }

    [Fact]
    public void Diff_FullDiff_CombinesAllLayers()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => InsertRow(db, "items", 1, "alpha"));
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db => InsertRow(db, "items", 1, "alpha"));

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right);

        Assert.NotNull(result.Schema);
        Assert.NotNull(result.Ledger);
        Assert.NotEmpty(result.Tables);
        Assert.True(result.AreIdentical);
    }

    [Fact]
    public void Diff_TableFilter_OnlyDiffsSpecifiedTables()
    {
        var pathL = TempPath();
        var pathR = TempPath();
        using var dbL = CreateDbWithTable(pathL, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db =>
            {
                ExecuteDdl(db, "CREATE TABLE logs (id INTEGER PRIMARY KEY, msg TEXT)");
                InsertRow(db, "items", 1, "a");
                InsertRow(db, "logs", 1, "x");
            });
        using var dbR = CreateDbWithTable(pathR, "items",
            "CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT)",
            db =>
            {
                ExecuteDdl(db, "CREATE TABLE logs (id INTEGER PRIMARY KEY, msg TEXT)");
                InsertRow(db, "items", 1, "a");
                InsertRow(db, "logs", 1, "CHANGED");
            });

        var left = new ArcHandle("left.arc", dbL);
        var right = new ArcHandle("right.arc", dbR);

        var result = ArcDiffer.Diff(left, right, new ArcDiffOptions
        {
            Scope = DiffScope.Data,
            TableFilter = new List<string> { "items" }
        });

        Assert.Single(result.Tables);
        Assert.Equal("items", result.Tables[0].TableName);
        Assert.True(result.Tables[0].IsIdentical);
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
