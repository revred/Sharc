// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class VectorWriterTests : IDisposable
{
    private readonly string _dbPath;
    private SharcDatabase _db;
    private const int VectorDim = 4;

    public VectorWriterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_vecw_{Guid.NewGuid()}.db");
        _db = SharcDatabase.Create(_dbPath);

        using var tx = _db.BeginTransaction();
        tx.Execute("CREATE TABLE embeddings (id INTEGER PRIMARY KEY, label TEXT, category TEXT, score REAL, vector BLOB)");
        tx.Commit();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void InsertVector_RoundTrip_FindableByNearestTo()
    {
        float[] vec = [1.0f, 0.0f, 0.0f, 0.0f];

        using (var writer = SharcWriter.From(_db))
        {
            writer.InsertVector("embeddings", "vector", vec,
                ("id", 1L),
                ("label", "test_item"),
                ("category", "A"),
                ("score", 0.95));
        }

        // Re-open for read
        _db.Dispose();
        _db = SharcDatabase.Open(_dbPath);

        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, 1);

        Assert.Equal(1, results.Count);
        Assert.Equal(1L, results[0].RowId);
    }

    [Fact]
    public void InsertVector_WithMetadata_PreservesColumns()
    {
        float[] vec = [0.5f, 0.5f, 0.0f, 0.0f];

        using (var writer = SharcWriter.From(_db))
        {
            writer.InsertVector("embeddings", "vector", vec,
                ("label", "hello_world"),
                ("category", "B"),
                ("score", 3.14));
        }

        // Read back metadata (id is auto-assigned rowid)
        using var reader = _db.CreateReader("embeddings");
        Assert.True(reader.Read());
        Assert.Equal("hello_world", reader.GetString(1)); // label
        Assert.Equal("B", reader.GetString(2));       // category
        Assert.Equal(3.14, reader.GetDouble(3), 2);   // score

        // Verify vector bytes
        var blobSpan = reader.GetBlobSpan(4);
        var decoded = BlobVectorCodec.Decode(blobSpan);
        Assert.Equal(0.5f, decoded[0]);
        Assert.Equal(0.5f, decoded[1]);
    }

    [Fact]
    public void InsertVector_MultipleRows_AllRetrievable()
    {
        using (var writer = SharcWriter.From(_db))
        {
            for (int i = 0; i < 5; i++)
            {
                float[] vec = [i * 0.1f, 1.0f - i * 0.1f, 0.0f, 0.0f];
                writer.InsertVector("embeddings", "vector", vec,
                    ("id", (long)(i + 1)),
                    ("label", $"item_{i}"),
                    ("category", i % 2 == 0 ? "A" : "B"),
                    ("score", (double)(i * 10)));
            }
        }

        // Re-open for read
        _db.Dispose();
        _db = SharcDatabase.Open(_dbPath);

        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [0.0f, 1.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, 5);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void InsertVector_MismatchedDimensions_ThrowsArgumentException()
    {
        using var writer = SharcWriter.From(_db);

        // First insert: dim=4 — sets the baseline
        float[] vec4 = [1.0f, 0.0f, 0.0f, 0.0f];
        writer.InsertVector("embeddings", "vector", vec4,
            ("label", "first"));

        // Second insert: dim=3 — must throw
        float[] vec3 = [1.0f, 0.0f, 0.0f];
        var ex = Assert.Throws<ArgumentException>(() =>
            writer.InsertVector("embeddings", "vector", vec3,
                ("label", "second")));

        Assert.Contains("3", ex.Message);
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public void InsertVector_FirstInsert_SetsTableDimensions()
    {
        using var writer = SharcWriter.From(_db);

        // First insert into empty table — any dimension accepted
        float[] vec4 = [1.0f, 2.0f, 3.0f, 4.0f];
        long rowId1 = writer.InsertVector("embeddings", "vector", vec4,
            ("label", "first"));
        Assert.True(rowId1 > 0);

        // Second insert with same dimension — succeeds
        float[] vec4b = [5.0f, 6.0f, 7.0f, 8.0f];
        long rowId2 = writer.InsertVector("embeddings", "vector", vec4b,
            ("label", "second"));
        Assert.True(rowId2 > rowId1);
    }

    [Fact]
    public void InsertVector_EmptyVector_ThrowsArgumentException()
    {
        using var writer = SharcWriter.From(_db);

        float[] empty = [];
        Assert.Throws<ArgumentException>(() =>
            writer.InsertVector("embeddings", "vector", empty,
                ("label", "bad")));
    }
}
