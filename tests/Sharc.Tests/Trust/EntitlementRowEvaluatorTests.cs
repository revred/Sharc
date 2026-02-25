// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public sealed class EntitlementRowEvaluatorTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;

    public EntitlementRowEvaluatorTests()
    {
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);

        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE docs (id INTEGER PRIMARY KEY, title TEXT, owner_tag TEXT, body TEXT)");
        tx.Commit();

        // Seed multi-tenant data
        SeedRows();
    }

    private void SeedRows()
    {
        // 3 rows for tenant:acme, 2 for tenant:globex, 1 for role:admin
        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 1),
            (Sharc.Core.ColumnValue)"Project Alpha",
            (Sharc.Core.ColumnValue)"tenant:acme",
            (Sharc.Core.ColumnValue)"Acme document 1");

        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 2),
            (Sharc.Core.ColumnValue)"Project Beta",
            (Sharc.Core.ColumnValue)"tenant:acme",
            (Sharc.Core.ColumnValue)"Acme document 2");

        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 3),
            (Sharc.Core.ColumnValue)"Globex Plan",
            (Sharc.Core.ColumnValue)"tenant:globex",
            (Sharc.Core.ColumnValue)"Globex document 1");

        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 4),
            (Sharc.Core.ColumnValue)"Globex Budget",
            (Sharc.Core.ColumnValue)"tenant:globex",
            (Sharc.Core.ColumnValue)"Globex document 2");

        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 5),
            (Sharc.Core.ColumnValue)"System Config",
            (Sharc.Core.ColumnValue)"role:admin",
            (Sharc.Core.ColumnValue)"Admin-only document");

        _writer.Insert("docs",
            Sharc.Core.ColumnValue.FromInt64(6, 6),
            (Sharc.Core.ColumnValue)"Acme Roadmap",
            (Sharc.Core.ColumnValue)"tenant:acme",
            (Sharc.Core.ColumnValue)"Acme document 3");
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    // ── Single Tag Tests ──

    [Fact]
    public void AcmeTenant_SeesOnlyAcmeRows()
    {
        var context = new SharcEntitlementContext("tenant:acme");
        // owner_tag is column ordinal 2 (id=0, title=1, owner_tag=2, body=3)
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        var titles = ReadAllTitles(reader);

        Assert.Equal(3, titles.Count);
        Assert.Contains("Project Alpha", titles);
        Assert.Contains("Project Beta", titles);
        Assert.Contains("Acme Roadmap", titles);
    }

    [Fact]
    public void GlobexTenant_SeesOnlyGlobexRows()
    {
        var context = new SharcEntitlementContext("tenant:globex");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        var titles = ReadAllTitles(reader);

        Assert.Equal(2, titles.Count);
        Assert.Contains("Globex Plan", titles);
        Assert.Contains("Globex Budget", titles);
    }

    [Fact]
    public void AdminRole_SeesOnlyAdminRows()
    {
        var context = new SharcEntitlementContext("role:admin");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        var titles = ReadAllTitles(reader);

        Assert.Single(titles);
        Assert.Contains("System Config", titles);
    }

    // ── Multiple Tag Tests ──

    [Fact]
    public void MultipleTags_SeesUnion()
    {
        // Agent has both tenant:acme and role:admin entitlements
        var context = new SharcEntitlementContext("tenant:acme", "role:admin");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        var titles = ReadAllTitles(reader);

        Assert.Equal(4, titles.Count); // 3 acme + 1 admin
        Assert.Contains("Project Alpha", titles);
        Assert.Contains("System Config", titles);
    }

    // ── No Matching Tag ──

    [Fact]
    public void NonExistentTag_SeesNoRows()
    {
        var context = new SharcEntitlementContext("tenant:evil");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        Assert.False(reader.Read());
    }

    // ── Empty Context ──

    [Fact]
    public void EmptyContext_SeesNoRows()
    {
        var context = new SharcEntitlementContext();
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var reader = CreateReaderWithEvaluator(evaluator);
        Assert.False(reader.Read());
    }

    // ── Combined with Filter ──

    [Fact]
    public void EvaluatorPlusFilter_BothApplied()
    {
        // Filter: id > 2, Entitlement: tenant:acme
        // Expected: only row 6 (Acme Roadmap, id=6) — rows 1,2 excluded by filter, row 5 excluded by tag
        var context = new SharcEntitlementContext("tenant:acme");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);
        var filter = FilterStar.Column("id").Gt(2L);

        using var jit = _db.Jit("docs");
        var compiledFilter = FilterTreeCompiler.CompileBaked(
            filter,
            jit.Table!.Columns,
            0); // id is column 0 = INTEGER PRIMARY KEY

        using var reader = CreateReaderWithEvaluatorAndFilter(evaluator, compiledFilter);
        var titles = ReadAllTitles(reader);

        Assert.Single(titles);
        Assert.Contains("Acme Roadmap", titles);
    }

    // ── Helpers ──

    private SharcDataReader CreateReaderWithEvaluator(EntitlementRowEvaluator evaluator)
    {
        var schema = _db.Schema;
        var table = schema.GetTable("docs");
        var cursor = _db.CreateTableCursorForPrepared(table);

        return new SharcDataReader(cursor, _db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            BTreeReader = _db.BTreeReaderInternal,
            TableIndexes = table.Indexes,
            RowAccessEvaluator = evaluator
        });
    }

    private SharcDataReader CreateReaderWithEvaluatorAndFilter(
        EntitlementRowEvaluator evaluator, IFilterNode filterNode)
    {
        var schema = _db.Schema;
        var table = schema.GetTable("docs");
        var cursor = _db.CreateTableCursorForPrepared(table);

        return new SharcDataReader(cursor, _db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            BTreeReader = _db.BTreeReaderInternal,
            TableIndexes = table.Indexes,
            FilterNode = filterNode,
            RowAccessEvaluator = evaluator
        });
    }

    private static List<string> ReadAllTitles(SharcDataReader reader)
    {
        var titles = new List<string>();
        while (reader.Read())
            titles.Add(reader.GetString(1)); // title is ordinal 1
        return titles;
    }
}
