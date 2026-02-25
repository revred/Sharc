// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Trust;
using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class VectorEntitlementTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private const int VectorDim = 4;

    public VectorEntitlementTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_ent_{Guid.NewGuid()}.db");

        var db = SharcDatabase.Create(_dbPath);

        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE docs (id INTEGER PRIMARY KEY, title TEXT, owner_tag TEXT, embedding BLOB)");
            tx.Commit();
        }

        // 6 rows: 3 owned by tenant:acme, 3 by tenant:globex
        var rows = new (string Title, string Tag, float[] Vec)[]
        {
            ("Acme Report 1", "tenant:acme", [1.0f, 0.0f, 0.0f, 0.0f]),
            ("Acme Report 2", "tenant:acme", [0.9f, 0.1f, 0.0f, 0.0f]),
            ("Acme Report 3", "tenant:acme", [0.8f, 0.2f, 0.0f, 0.0f]),
            ("Globex Doc 1", "tenant:globex", [0.0f, 1.0f, 0.0f, 0.0f]),
            ("Globex Doc 2", "tenant:globex", [0.0f, 0.9f, 0.1f, 0.0f]),
            ("Globex Doc 3", "tenant:globex", [0.0f, 0.0f, 1.0f, 0.0f]),
        };

        using (var writer = SharcWriter.From(db))
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var (title, tag, vec) = rows[i];
                byte[] vecBytes = BlobVectorCodec.Encode(vec);
                byte[] titleBytes = Encoding.UTF8.GetBytes(title);
                byte[] tagBytes = Encoding.UTF8.GetBytes(tag);

                writer.Insert("docs",
                    ColumnValue.Null(), // id
                    ColumnValue.Text(2L * titleBytes.Length + 13, titleBytes),
                    ColumnValue.Text(2L * tagBytes.Length + 13, tagBytes),
                    ColumnValue.Blob(2L * vecBytes.Length + 12, vecBytes));
            }
        }

        db.Dispose();
        _db = SharcDatabase.Open(_dbPath);
    }

    private static AgentInfo MakeAgent(string id, string readScope) =>
        new(id, AgentClass.User, [], 0, "*", readScope, 0, 0, "", false, []);

    // ── VectorQuery + Agent ─────────────────────────────────────

    [Fact]
    public void VectorQuery_WithAgent_FullAccess_Works()
    {
        var agent = MakeAgent("full", "docs.*");
        using var vq = _db.Vector("docs", "embedding");
        vq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, k: 10);

        Assert.Equal(6, results.Count);
    }

    [Fact]
    public void VectorQuery_WithAgent_NoTableAccess_Throws()
    {
        var agent = MakeAgent("restricted", "other_table.*");
        using var vq = _db.Vector("docs", "embedding");
        vq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<UnauthorizedAccessException>(() => vq.NearestTo(query, k: 5));
    }

    [Fact]
    public void VectorQuery_WithAgent_NoColumnAccess_Throws()
    {
        // Agent can read docs.title but NOT docs.embedding
        var agent = MakeAgent("col-restricted", "docs.title");
        using var vq = _db.Vector("docs", "embedding");
        vq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<UnauthorizedAccessException>(() => vq.NearestTo(query, k: 5));
    }

    [Fact]
    public void VectorQuery_WithAgent_MetadataColumnDenied_Throws()
    {
        // Agent can read docs.embedding but NOT docs.title
        var agent = MakeAgent("partial", "docs.embedding");
        using var vq = _db.Vector("docs", "embedding");
        vq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Requesting "title" metadata should be denied
        Assert.Throws<UnauthorizedAccessException>(() => vq.NearestTo(query, k: 5, "title"));
    }

    [Fact]
    public void VectorQuery_WithoutAgent_NoEnforcement()
    {
        using var vq = _db.Vector("docs", "embedding");
        // No agent set — no enforcement
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 10);
        Assert.Equal(6, results.Count);
    }

    // ── VectorQuery + RowEvaluator ──────────────────────────────

    [Fact]
    public void VectorQuery_WithRowEvaluator_FiltersRows()
    {
        var context = new SharcEntitlementContext("tenant:acme");
        // owner_tag is column ordinal 2 (id=0, title=1, owner_tag=2, embedding=3)
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var vq = _db.Vector("docs", "embedding");
        vq.WithRowEvaluator(evaluator);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, k: 10);

        // Only 3 acme rows should be visible
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void VectorQuery_WithRowEvaluator_GlobexAgent_SeesOnlyGlobex()
    {
        var context = new SharcEntitlementContext("tenant:globex");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var vq = _db.Vector("docs", "embedding");
        vq.WithRowEvaluator(evaluator);

        float[] query = [0.0f, 1.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, k: 10);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void VectorQuery_WithinDistance_RespectsRowEvaluator()
    {
        var context = new SharcEntitlementContext("tenant:acme");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var vq = _db.Vector("docs", "embedding", DistanceMetric.Euclidean);
        vq.WithRowEvaluator(evaluator);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = vq.WithinDistance(query, maxDistance: 1000f);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void VectorQuery_AgentAndRowEvaluator_BothApplied()
    {
        var agent = MakeAgent("full", "docs.*");
        var context = new SharcEntitlementContext("tenant:acme");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var vq = _db.Vector("docs", "embedding");
        vq.WithAgent(agent).WithRowEvaluator(evaluator);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = vq.NearestTo(query, k: 10);

        // Agent allows table access, evaluator limits to 3 acme rows
        Assert.Equal(3, results.Count);
    }

    // ── HybridQuery + Agent ─────────────────────────────────────

    [Fact]
    public void HybridQuery_WithAgent_FullAccess_Works()
    {
        var agent = MakeAgent("full", "docs.*");
        using var hq = _db.Hybrid("docs", "embedding", "title");
        hq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = hq.Search(query, "Report", k: 10);

        Assert.True(results.Count > 0);
    }

    [Fact]
    public void HybridQuery_WithAgent_NoTableAccess_Throws()
    {
        var agent = MakeAgent("restricted", "other.*");
        using var hq = _db.Hybrid("docs", "embedding", "title");
        hq.WithAgent(agent);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        Assert.Throws<UnauthorizedAccessException>(() => hq.Search(query, "Report", k: 5));
    }

    // ── HybridQuery + RowEvaluator ──────────────────────────────

    [Fact]
    public void HybridQuery_WithRowEvaluator_FiltersRows()
    {
        var context = new SharcEntitlementContext("tenant:acme");
        var evaluator = new EntitlementRowEvaluator(context, tagColumnOrdinal: 2, columnCount: 4);

        using var hq = _db.Hybrid("docs", "embedding", "title");
        hq.WithRowEvaluator(evaluator);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = hq.Search(query, "Report", k: 10);

        // Only acme rows visible; all 3 have "Report" in title
        Assert.True(results.Count > 0);
        Assert.True(results.Count <= 3);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }
}
