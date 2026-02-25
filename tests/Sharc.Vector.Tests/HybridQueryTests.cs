// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class HybridQueryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private const int VectorDim = 4;

    public HybridQueryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_hybrid_{Guid.NewGuid()}.db");

        var db = SharcDatabase.Create(_dbPath);

        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE documents (id INTEGER PRIMARY KEY, title TEXT, content TEXT, category TEXT, embedding BLOB)");
            tx.Commit();
        }

        // 10 rows with varied text and vectors so vector and text rankings differ
        var rows = new (string Title, string Content, string Category, float[] Vec)[]
        {
            ("Introduction to Neural Networks", "Neural networks are computing systems inspired by biological neural networks", "AI", new float[] { 1.0f, 0.0f, 0.0f, 0.0f }),
            ("Deep Learning Basics", "Deep learning uses neural networks with many layers for representation learning", "AI", new float[] { 0.9f, 0.1f, 0.0f, 0.0f }),
            ("Graph Databases", "Graph databases store data as nodes and edges for relationship queries", "DB", new float[] { 0.0f, 1.0f, 0.0f, 0.0f }),
            ("Machine Learning Overview", "Machine learning is a subset of artificial intelligence and data science", "AI", new float[] { 0.7f, 0.3f, 0.0f, 0.0f }),
            ("Natural Language Processing", "NLP uses neural networks for text analysis and language understanding", "AI", new float[] { 0.5f, 0.5f, 0.0f, 0.0f }),
            ("Relational Databases", "Relational databases use SQL for structured data management and queries", "DB", new float[] { 0.0f, 0.0f, 1.0f, 0.0f }),
            ("Computer Vision", "Computer vision applies deep learning to image recognition and object detection", "AI", new float[] { 0.3f, 0.0f, 0.0f, 0.7f }),
            ("Data Structures", "Data structures like trees and graphs organize data for efficient algorithms", "CS", new float[] { 0.0f, 0.0f, 0.0f, 1.0f }),
            ("Reinforcement Learning", "Reinforcement learning trains agents through rewards and neural network policies", "AI", new float[] { 0.6f, 0.2f, 0.1f, 0.1f }),
            ("Cloud Computing", "Cloud computing provides scalable infrastructure for distributed applications", "INFRA", new float[] { 0.0f, 0.0f, 0.5f, 0.5f }),
        };

        using (var writer = SharcWriter.From(db))
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var (title, content, category, vec) = rows[i];
                byte[] vecBytes = BlobVectorCodec.Encode(vec);
                long blobSt = 2L * vecBytes.Length + 12;
                byte[] titleBytes = Encoding.UTF8.GetBytes(title);
                byte[] contentBytes = Encoding.UTF8.GetBytes(content);
                byte[] catBytes = Encoding.UTF8.GetBytes(category);

                writer.Insert("documents",
                    ColumnValue.Null(), // id (rowid alias)
                    ColumnValue.Text(2L * titleBytes.Length + 13, titleBytes),
                    ColumnValue.Text(2L * contentBytes.Length + 13, contentBytes),
                    ColumnValue.Text(2L * catBytes.Length + 13, catBytes),
                    ColumnValue.Blob(blobSt, vecBytes));
            }
        }

        db.Dispose();
        _db = SharcDatabase.Open(_dbPath);
    }

    [Fact]
    public void Search_VectorAndText_ReturnsFusedResults()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural networks", k: 5);

        Assert.True(results.Count > 0);
        Assert.True(results.Count <= 5);
        Assert.All(results.Matches, m => Assert.True(m.Score > 0));
    }

    [Fact]
    public void Search_TopResultsAppearInBothLists()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural networks", k: 3);

        // Row 1 ("Introduction to Neural Networks") should rank high in both:
        // - Vector: embedding [1,0,0,0] is closest to query [1,0,0,0]
        // - Text: content contains "neural networks"
        // So the top result should have both VectorRank > 0 and TextRank > 0
        Assert.True(results[0].VectorRank > 0, "Top result should have a vector rank");
        Assert.True(results[0].TextRank > 0, "Top result should have a text rank");
    }

    [Fact]
    public void Search_KLargerThanTable_ReturnsAllMatched()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural networks", k: 100);

        Assert.True(results.Count <= 10);
        Assert.True(results.Count > 0);
    }

    [Fact]
    public void Search_NoTextMatch_ReturnsVectorOnly()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "zzzznonexistentterm", k: 5);

        Assert.True(results.Count > 0, "Should still return vector results");
        Assert.All(results.Matches, m => Assert.Equal(0, m.TextRank));
    }

    [Fact]
    public void Search_WithMetadataProjection_ReturnsExtractedMetadata()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural", k: 3, "title", "category");

        Assert.True(results.Count > 0);
        foreach (var match in results.Matches)
        {
            Assert.NotNull(match.Metadata);
            Assert.True(match.Metadata!.ContainsKey("title"));
            Assert.True(match.Metadata.ContainsKey("category"));
            Assert.IsType<string>(match.Metadata["title"]);
            Assert.IsType<string>(match.Metadata["category"]);
        }
    }

    [Fact]
    public void Search_DimensionMismatch_ThrowsArgumentException()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] wrongDim = [1.0f, 2.0f]; // 2-dim, but vectors are 4-dim

        Assert.Throws<ArgumentException>(() => hq.Search(wrongDim, "neural", k: 5));
    }

    [Fact]
    public void Search_DisposedQuery_ThrowsObjectDisposedException()
    {
        var hq = _db.Hybrid("documents", "embedding", "content");
        hq.Dispose();
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ObjectDisposedException>(() => hq.Search(query, "neural", k: 5));
    }

    [Fact]
    public void Search_WithPreFilter_FiltersBeforeBothSearches()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        hq.Where(FilterStar.Column("category").Eq("AI"));
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural", k: 10);

        // Only AI-category rows (rows 1,2,4,5,7,9 = 6 rows)
        // All results should come from AI category
        Assert.True(results.Count > 0);
        Assert.True(results.Count <= 6, "Should only return AI-category rows");
    }

    [Fact]
    public void Search_ClearFilters_ResetsState()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Filter to category AI
        hq.Where(FilterStar.Column("category").Eq("DB"));
        var filtered = hq.Search(query, "data", k: 10);

        // Clear and search all
        hq.ClearFilters();
        var all = hq.Search(query, "data", k: 10);

        Assert.True(all.Count >= filtered.Count);
    }

    [Fact]
    public void Search_ResultsOrderedByScoreDescending()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = hq.Search(query, "neural networks", k: 10);

        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"Results not sorted: [{i - 1}].Score={results[i - 1].Score} < [{i}].Score={results[i].Score}");
    }

    [Fact]
    public void Search_VectorRankZero_MeansAbsentFromVectorResults()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        // Use a vector pointing away from most rows
        float[] query = [0.0f, 0.0f, 0.0f, 1.0f];

        // Search for "neural" which appears in rows with vectors near [1,0,0,0]
        // Some text matches may not be in vector top-K
        var results = hq.Search(query, "neural", k: 10);

        // Verify the rank semantics: 0 means absent, >0 means present
        foreach (var match in results.Matches)
        {
            Assert.True(match.VectorRank >= 0);
            Assert.True(match.TextRank >= 0);
            // At least one rank must be non-zero (otherwise why is it in results?)
            Assert.True(match.VectorRank > 0 || match.TextRank > 0);
        }
    }

    [Fact]
    public void Search_TextRankZero_MeansAbsentFromTextResults()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // "zzzznonexistent" won't match any text
        var results = hq.Search(query, "zzzznonexistent", k: 5);

        Assert.All(results.Matches, m => Assert.Equal(0, m.TextRank));
    }

    [Fact]
    public void Search_EmptyQueryText_ThrowsArgumentException()
    {
        using var hq = _db.Hybrid("documents", "embedding", "content");
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ArgumentException>(() => hq.Search(query, "", k: 5));
    }

    [Fact]
    public void Search_SingleRow_ReturnsIt()
    {
        // Create a separate single-row database
        string singlePath = Path.Combine(Path.GetTempPath(), $"sharc_hybrid_single_{Guid.NewGuid()}.db");
        try
        {
            var sdb = SharcDatabase.Create(singlePath);
            using (var tx = sdb.BeginTransaction())
            {
                tx.Execute("CREATE TABLE docs (id INTEGER PRIMARY KEY, text_col TEXT, vec_col BLOB)");
                tx.Commit();
            }
            using (var writer = SharcWriter.From(sdb))
            {
                float[] vec = [1.0f, 0.0f, 0.0f, 0.0f];
                byte[] vecBytes = BlobVectorCodec.Encode(vec);
                byte[] textBytes = Encoding.UTF8.GetBytes("hello world");
                writer.Insert("docs",
                    ColumnValue.Null(),
                    ColumnValue.Text(2L * textBytes.Length + 13, textBytes),
                    ColumnValue.Blob(2L * vecBytes.Length + 12, vecBytes));
            }
            sdb.Dispose();

            using var readDb = SharcDatabase.Open(singlePath);
            using var hq = readDb.Hybrid("docs", "vec_col", "text_col");
            float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

            var results = hq.Search(query, "hello", k: 5);

            Assert.Equal(1, results.Count);
            Assert.True(results[0].VectorRank > 0);
            Assert.True(results[0].TextRank > 0);
        }
        finally
        {
            try { File.Delete(singlePath); } catch { }
            try { File.Delete(singlePath + ".journal"); } catch { }
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }
}
