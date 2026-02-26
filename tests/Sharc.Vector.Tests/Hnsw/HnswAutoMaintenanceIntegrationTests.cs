// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public sealed class HnswAutoMaintenanceIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;

    public HnswAutoMaintenanceIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_hnsw_autosync_{Guid.NewGuid()}.db");
        var db = SharcDatabase.Create(_dbPath);

        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE docs (id INTEGER PRIMARY KEY, tag TEXT, embedding BLOB)");
            tx.Commit();
        }

        _db = db;
    }

    [Fact]
    public void AutoSync_InsertAndDelete_UpdatesIndexOnCommit()
    {
        using var writer = SharcWriter.From(_db);
        writer.Insert("docs", MakeRow("north", new float[] { 0f, 1f }));
        writer.Insert("docs", MakeRow("west", new float[] { -1f, 0f }));

        using var index = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 7 }, persist: false);

        long rowId = writer.Insert("docs", MakeRow("east", new float[] { 1f, 0f }));
        var nearest = index.Search(new float[] { 1f, 0f }, k: 1);
        Assert.Equal(rowId, nearest[0].RowId);

        Assert.True(writer.Delete("docs", rowId));
        Assert.Equal(2, index.Count);

        var afterDelete = index.Search(new float[] { 1f, 0f }, k: 2);
        Assert.DoesNotContain(afterDelete.Matches, m => m.RowId == rowId);
    }

    [Fact]
    public void AutoSync_UpdateOnCommit_AndRollbackDoesNotMutateIndex()
    {
        using var writer = SharcWriter.From(_db);
        writer.Insert("docs", MakeRow("north", new float[] { 0f, 1f }));
        long rowId2 = writer.Insert("docs", MakeRow("west", new float[] { -1f, 0f }));

        using var index = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 11 }, persist: false);

        using (var tx = writer.BeginTransaction())
        {
            Assert.True(tx.Update("docs", rowId2, MakeRow("east", new float[] { 1f, 0f })));

            // Index maintenance is commit-time; uncommitted mutation is invisible.
            var beforeCommit = index.Search(new float[] { 1f, 0f }, k: 1);
            Assert.NotEqual(rowId2, beforeCommit[0].RowId);

            tx.Commit();
        }

        var afterCommit = index.Search(new float[] { 1f, 0f }, k: 1);
        Assert.Equal(rowId2, afterCommit[0].RowId);

        long rolledBackRowId;
        using (var tx = writer.BeginTransaction())
        {
            rolledBackRowId = tx.Insert("docs", MakeRow("temp", new float[] { 0.99f, 0.01f }));

            // Still unchanged before commit.
            var duringTx = index.Search(new float[] { 1f, 0f }, k: 1);
            Assert.Equal(rowId2, duringTx[0].RowId);

            tx.Rollback();
        }

        var afterRollback = index.Search(new float[] { 1f, 0f }, k: 4);
        Assert.DoesNotContain(afterRollback.Matches, m => m.RowId == rolledBackRowId);
    }

    [Fact]
    public void AutoSync_InsertThenDeleteSameRowInTransaction_DoesNotLeaveIndexEntry()
    {
        using var writer = SharcWriter.From(_db);
        writer.Insert("docs", MakeRow("north", new float[] { 0f, 1f }));

        using var index = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 19 }, persist: false);

        long transientRowId;
        using (var tx = writer.BeginTransaction())
        {
            transientRowId = tx.Insert("docs", MakeRow("temp", new float[] { 1f, 0f }));
            Assert.True(tx.Delete("docs", transientRowId));
            tx.Commit();
        }

        Assert.Equal(1, index.Count);
        var results = index.Search(new float[] { 1f, 0f }, k: 4);
        Assert.DoesNotContain(results.Matches, m => m.RowId == transientRowId);
    }

    [Fact]
    public void AutoSync_MultipleIndexesOnSameColumn_AreUpdatedOnCommit()
    {
        using var writer = SharcWriter.From(_db);
        writer.Insert("docs", MakeRow("origin", new float[] { 0f, 0f }));

        using var indexA = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 23 }, persist: false);
        using var indexB = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 29 }, persist: false);

        long rowId = writer.Insert("docs", MakeRow("east", new float[] { 1f, 0f }));
        Assert.Equal(rowId, indexA.Search(new float[] { 1f, 0f }, k: 1)[0].RowId);
        Assert.Equal(rowId, indexB.Search(new float[] { 1f, 0f }, k: 1)[0].RowId);

        Assert.True(writer.Delete("docs", rowId));
        Assert.DoesNotContain(indexA.Search(new float[] { 1f, 0f }, k: 4).Matches, m => m.RowId == rowId);
        Assert.DoesNotContain(indexB.Search(new float[] { 1f, 0f }, k: 4).Matches, m => m.RowId == rowId);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-journal"); } catch { }
    }

    private static ColumnValue[] MakeRow(string tag, float[] vector)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        byte[] vectorBytes = BlobVectorCodec.Encode(vector);
        long vectorSerialType = 2L * vectorBytes.Length + 12;

        return
        [
            ColumnValue.Null(), // rowid-alias column
            ColumnValue.Text(2L * tagBytes.Length + 13, tagBytes),
            ColumnValue.Blob(vectorSerialType, vectorBytes)
        ];
    }
}
