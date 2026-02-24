/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc;
using Sharc.Graph;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public class GetContextTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;
    private readonly SharcContextGraph _graph;

    public GetContextTests()
    {
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);

        // Create graph tables
        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, updated_at INTEGER, tokens INTEGER, alias TEXT)");
        tx.Execute("CREATE TABLE _relations (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, weight REAL)");
        tx.Commit();

        // Seed graph data
        using var gw = new GraphWriter(_writer);
        gw.Intern("proj", new NodeKey(1), ConceptKind.Project, """{"name":"MyProject"}""", tokens: 10);
        gw.Intern("cls", new NodeKey(2), ConceptKind.Class, """{"name":"UserService"}""", tokens: 50);
        gw.Intern("meth", new NodeKey(3), ConceptKind.Method, """{"name":"GetUser"}""", tokens: 30);
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);
        gw.Link("e2", new NodeKey(2), new NodeKey(3), RelationKind.Defines);

        _graph = new SharcContextGraph(_db.BTreeReader, new NativeSchemaAdapter());
        _graph.Initialize();
    }

    public void Dispose()
    {
        _graph.Dispose();
        _writer.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void GetContext_SingleNode_ReturnsSummary()
    {
        var summary = _graph.GetContext(new NodeKey(1), maxDepth: 0);

        Assert.NotNull(summary);
        Assert.Single(summary.IncludedRecords);
        Assert.Contains("MyProject", summary.SummaryText);
        Assert.True(summary.TokenCount > 0);
    }

    [Fact]
    public void GetContext_WithEdges_IncludesRelationships()
    {
        var summary = _graph.GetContext(new NodeKey(1), maxDepth: 2);

        Assert.NotNull(summary);
        Assert.Equal(3, summary.IncludedRecords.Count);
        Assert.Contains("UserService", summary.SummaryText);
        Assert.Contains("GetUser", summary.SummaryText);
    }

    [Fact]
    public void GetContext_MaxTokens_TruncatesResult()
    {
        // With a very small token budget, should include fewer records
        var summary = _graph.GetContext(new NodeKey(1), maxDepth: 2, maxTokens: 20);

        Assert.NotNull(summary);
        Assert.True(summary.TokenCount <= 20);
        // Should include at least the root but possibly not all 3 nodes
        Assert.True(summary.IncludedRecords.Count >= 1);
        Assert.True(summary.IncludedRecords.Count <= 3);
    }

    [Fact]
    public void GetContext_NonExistentNode_ReturnsEmptySummary()
    {
        var summary = _graph.GetContext(new NodeKey(999), maxDepth: 2);

        Assert.NotNull(summary);
        Assert.Empty(summary.IncludedRecords);
        Assert.Equal(0, summary.TokenCount);
    }
}
