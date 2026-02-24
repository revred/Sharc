// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Xunit;

namespace Sharc.Arc.Tests;

/// <summary>
/// Tests for <see cref="FusedArcContext"/> — multi-arc fusion engine.
/// </summary>
public sealed class FusedArcContextTests : IDisposable
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
        var p = Path.Combine(Path.GetTempPath(), $"sharc_fused_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        return p;
    }

    private ArcHandle CreateArcWithData(string arcName, string tableName, string ddl,
        params (long id, string val)[] rows)
    {
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        using (var tx = db.BeginTransaction())
        {
            tx.Execute(ddl);
            tx.Commit();
        }

        using var writer = SharcWriter.From(db);
        foreach (var (id, val) in rows)
        {
            writer.Insert(tableName,
                ColumnValue.FromInt64(1, id),
                ColumnValue.Text(val.Length, Encoding.UTF8.GetBytes(val)));
        }

        return new ArcHandle(arcName, db);
    }

    [Fact]
    public void Mount_SingleArc_Queryable()
    {
        var arc = CreateArcWithData("test.arc", "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)",
            (1, "Alice"), (2, "Bob"));

        using var fused = new FusedArcContext();
        fused.Mount(arc);

        Assert.Equal(1, fused.Count);

        var rows = fused.Query("users");
        Assert.Equal(2, rows.Count);
        Assert.Equal("test.arc", rows[0].SourceArc);
    }

    [Fact]
    public void Mount_MultipleArcs_UnionQuery()
    {
        var arc1 = CreateArcWithData("dept_a.arc", "employees",
            "CREATE TABLE employees (id INTEGER PRIMARY KEY, name TEXT)",
            (1, "Alice"), (2, "Bob"));

        var arc2 = CreateArcWithData("dept_b.arc", "employees",
            "CREATE TABLE employees (id INTEGER PRIMARY KEY, name TEXT)",
            (1, "Charlie"), (2, "Diana"));

        using var fused = new FusedArcContext();
        fused.Mount(arc1);
        fused.Mount(arc2);

        var rows = fused.Query("employees");
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, r => r.SourceArc == "dept_a.arc");
        Assert.Contains(rows, r => r.SourceArc == "dept_b.arc");
    }

    [Fact]
    public void Query_MissingTable_ReturnsEmpty()
    {
        var arc = CreateArcWithData("test.arc", "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)",
            (1, "Alice"));

        using var fused = new FusedArcContext();
        fused.Mount(arc);

        var rows = fused.Query("nonexistent");
        Assert.Empty(rows);
    }

    [Fact]
    public void Query_MaxRows_LimitsPerArc()
    {
        var arc = CreateArcWithData("test.arc", "data",
            "CREATE TABLE data (id INTEGER PRIMARY KEY, val TEXT)",
            (1, "A"), (2, "B"), (3, "C"));

        using var fused = new FusedArcContext();
        fused.Mount(arc);

        var rows = fused.Query("data", maxRows: 2);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void DiscoverTables_ReturnsUserTables()
    {
        var arc1 = CreateArcWithData("a.arc", "users",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)", (1, "x"));
        var arc2 = CreateArcWithData("b.arc", "orders",
            "CREATE TABLE orders (id INTEGER PRIMARY KEY, item TEXT)", (1, "y"));

        using var fused = new FusedArcContext();
        fused.Mount(arc1);
        fused.Mount(arc2);

        var tables = fused.DiscoverTables();
        Assert.True(tables.ContainsKey("users"));
        Assert.True(tables.ContainsKey("orders"));
        Assert.Contains("a.arc", tables["users"]);
        Assert.Contains("b.arc", tables["orders"]);
    }

    [Fact]
    public void FindArcsWithTable_ReturnsCorrectArcs()
    {
        var arc1 = CreateArcWithData("a.arc", "shared",
            "CREATE TABLE shared (id INTEGER PRIMARY KEY, v TEXT)", (1, "x"));
        var arc2 = CreateArcWithData("b.arc", "unique",
            "CREATE TABLE unique (id INTEGER PRIMARY KEY, v TEXT)", (1, "y"));

        using var fused = new FusedArcContext();
        fused.Mount(arc1);
        fused.Mount(arc2);

        Assert.Single(fused.FindArcsWithTable("shared"));
        Assert.Single(fused.FindArcsWithTable("unique"));
        Assert.Empty(fused.FindArcsWithTable("missing"));
    }

    [Fact]
    public void Unmount_RemovesFromContext()
    {
        var arc = CreateArcWithData("test.arc", "data",
            "CREATE TABLE data (id INTEGER PRIMARY KEY, v TEXT)", (1, "x"));

        var fused = new FusedArcContext();
        var mounted = fused.Mount(arc);
        Assert.Equal(1, fused.Count);

        fused.Unmount(mounted);
        Assert.Equal(0, fused.Count);

        // Cleanup: dispose both manually since unmount doesn't dispose the handle
        arc.Dispose();
        fused.Dispose();
    }

    [Fact]
    public void Mount_WithAlias_UsesAlias()
    {
        var arc = CreateArcWithData("test.arc", "data",
            "CREATE TABLE data (id INTEGER PRIMARY KEY, v TEXT)", (1, "x"));

        using var fused = new FusedArcContext();
        fused.Mount(arc, alias: "my_data");

        var rows = fused.Query("data");
        Assert.Equal("my_data", rows[0].SourceArc);
    }

    [Fact]
    public void GetStats_ReturnsAggregate()
    {
        var arc1 = CreateArcWithData("a.arc", "t1",
            "CREATE TABLE t1 (id INTEGER PRIMARY KEY, v TEXT)", (1, "x"));
        var arc2 = CreateArcWithData("b.arc", "t2",
            "CREATE TABLE t2 (id INTEGER PRIMARY KEY, v TEXT)", (1, "y"));

        using var fused = new FusedArcContext();
        fused.Mount(arc1);
        fused.Mount(arc2);

        var stats = fused.GetStats();
        Assert.Equal(2, stats.ArcCount);
        Assert.True(stats.TotalTables >= 2);
    }

    [Fact]
    public void RowProvenance_ContainsRowId()
    {
        var arc = CreateArcWithData("test.arc", "data",
            "CREATE TABLE data (id INTEGER PRIMARY KEY, val TEXT)",
            (42, "hello"));

        using var fused = new FusedArcContext();
        fused.Mount(arc);

        var rows = fused.Query("data");
        Assert.Single(rows);
        Assert.True(rows[0].RowId > 0);
    }
}
