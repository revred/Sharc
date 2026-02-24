// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// F-7: Cursor-based pagination tests for .AfterRowId() on SharcDataReader.
/// Verifies keyset pagination where each page starts after the last seen rowid.
/// </summary>
public sealed class CursorPaginationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;

    public CursorPaginationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_cursor_test_{Guid.NewGuid():N}.arc");
        _db = SharcDatabase.Create(_dbPath);

        using var tx = _db.BeginTransaction();
        tx.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, value INTEGER)");
        tx.Commit();

        // Seed 20 rows
        using var sw = SharcWriter.From(_db);
        for (int i = 1; i <= 20; i++)
        {
            var nameBytes = Encoding.UTF8.GetBytes($"item_{i:D2}");
            sw.Insert("items",
                ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes),
                ColumnValue.FromInt64(1, i * 10));
        }
    }

    [Fact]
    public void AfterRowId_Zero_ReturnsAllRows()
    {
        using var reader = _db.CreateReader("items").AfterRowId(0);
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(20, count);
    }

    [Fact]
    public void AfterRowId_FirstRow_SkipsFirstRow()
    {
        using var reader = _db.CreateReader("items").AfterRowId(1);
        Assert.True(reader.Read());
        Assert.Equal("item_02", reader.GetString(0)); // first row skipped
    }

    [Fact]
    public void AfterRowId_Middle_SkipsFirstHalf()
    {
        using var reader = _db.CreateReader("items").AfterRowId(10);
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(10, count);
    }

    [Fact]
    public void AfterRowId_LastRow_ReturnsEmpty()
    {
        using var reader = _db.CreateReader("items").AfterRowId(20);
        Assert.False(reader.Read());
    }

    [Fact]
    public void AfterRowId_BeyondLastRow_ReturnsEmpty()
    {
        using var reader = _db.CreateReader("items").AfterRowId(100);
        Assert.False(reader.Read());
    }

    [Fact]
    public void AfterRowId_PagedIteration_CoversAllRows()
    {
        // Simulate keyset pagination with page size 5
        var allNames = new List<string>();
        long cursor = 0;

        while (true)
        {
            using var reader = _db.CreateReader("items").AfterRowId(cursor);
            int pageCount = 0;
            while (reader.Read() && pageCount < 5)
            {
                allNames.Add(reader.GetString(0));
                cursor = reader.RowId;
                pageCount++;
            }
            if (pageCount == 0) break;
        }

        Assert.Equal(20, allNames.Count);
        Assert.Equal("item_01", allNames[0]);
        Assert.Equal("item_20", allNames[19]);
    }

    [Fact]
    public void AfterRowId_PreservesRowId()
    {
        using var reader = _db.CreateReader("items").AfterRowId(5);
        Assert.True(reader.Read());
        Assert.Equal(6, reader.RowId);
    }

    [Fact]
    public void AfterRowId_WithNegativeValue_ReturnsAllRows()
    {
        using var reader = _db.CreateReader("items").AfterRowId(-1);
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(20, count);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + ".journal"); } catch { }
    }
}
