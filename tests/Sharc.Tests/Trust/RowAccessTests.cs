// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public sealed class RowAccessTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;

    public RowAccessTests()
    {
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);

        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, owner TEXT)");
        tx.Commit();

        // Seed 6 rows with alternating owners
        SeedRows();
    }

    private void SeedRows()
    {
        for (int i = 1; i <= 6; i++)
        {
            string owner = i % 2 == 0 ? "agent-B" : "agent-A";
            _writer.Insert("items",
                Sharc.Core.ColumnValue.FromInt64(6, i),
                (Sharc.Core.ColumnValue)owner);
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void ProcessRow_NoEvaluator_AllRowsPass()
    {
        int count = 0;
        using var reader = _db.CreateReader("items");
        while (reader.Read()) count++;

        Assert.Equal(6, count);
    }

    [Fact]
    public void ProcessRow_WithEvaluator_FilteredRowsSkipped()
    {
        // Evaluator that only allows odd rowIds
        var evaluator = new OddRowIdEvaluator();

        using var reader = CreateReaderWithEvaluator("items", evaluator);
        int count = 0;
        while (reader.Read()) count++;

        // Rows 1, 3, 5 pass (odd rowIds)
        Assert.Equal(3, count);
    }

    [Fact]
    public void ProcessRow_EvaluatorRejectsAll_ReturnsNoRows()
    {
        var evaluator = new RejectAllEvaluator();

        using var reader = CreateReaderWithEvaluator("items", evaluator);
        Assert.False(reader.Read());
    }

    [Fact]
    public void ProcessRow_EvaluatorAcceptsAll_AllRowsVisible()
    {
        var evaluator = new AcceptAllEvaluator();

        using var reader = CreateReaderWithEvaluator("items", evaluator);
        int count = 0;
        while (reader.Read()) count++;

        Assert.Equal(6, count);
    }

    [Fact]
    public void ProcessRow_EvaluatorWithFilter_BothApplied()
    {
        // Combined: FilterStar selects rows where id > 2, evaluator selects odd rowIds
        // Expected: rows 3 and 5 (id > 2 AND odd rowId)
        var evaluator = new OddRowIdEvaluator();
        var filter = FilterStar.Column("id").Gt(2L);

        using var jit = _db.Jit("items");
        var compiledFilter = FilterTreeCompiler.CompileBaked(
            filter,
            jit.Table!.Columns,
            0); // id is column 0 = INTEGER PRIMARY KEY

        using var reader = CreateReaderWithEvaluatorAndFilter("items", evaluator, compiledFilter);
        int count = 0;
        var rowIds = new List<long>();
        while (reader.Read())
        {
            count++;
            rowIds.Add(reader.RowId);
        }

        Assert.Equal(2, count);
        Assert.Contains(3L, rowIds);
        Assert.Contains(5L, rowIds);
    }

    // ── Helper: create reader with evaluator injected ──

    private SharcDataReader CreateReaderWithEvaluator(string tableName, IRowAccessEvaluator evaluator)
    {
        var schema = _db.Schema;
        var table = schema.GetTable(tableName);
        var cursor = _db.CreateTableCursorForPrepared(table);

        return new SharcDataReader(cursor, _db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            BTreeReader = _db.BTreeReaderInternal,
            TableIndexes = table.Indexes,
            RowAccessEvaluator = evaluator
        });
    }

    private SharcDataReader CreateReaderWithEvaluatorAndFilter(string tableName,
        IRowAccessEvaluator evaluator, IFilterNode filterNode)
    {
        var schema = _db.Schema;
        var table = schema.GetTable(tableName);
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

    // ── Test evaluator implementations ──

    private sealed class OddRowIdEvaluator : IRowAccessEvaluator
    {
        public bool CanAccess(ReadOnlySpan<byte> payload, long rowId) => rowId % 2 != 0;
    }

    private sealed class RejectAllEvaluator : IRowAccessEvaluator
    {
        public bool CanAccess(ReadOnlySpan<byte> payload, long rowId) => false;
    }

    private sealed class AcceptAllEvaluator : IRowAccessEvaluator
    {
        public bool CanAccess(ReadOnlySpan<byte> payload, long rowId) => true;
    }
}
