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

public class GraphWriterTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;

    public GraphWriterTests()
    {
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);

        // Create graph tables
        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, updated_at INTEGER, tokens INTEGER, alias TEXT)");
        tx.Execute("CREATE TABLE _relations (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, weight REAL)");
        tx.Commit();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Intern_NewConcept_CreatesNode()
    {
        using var gw = new GraphWriter(_writer);

        var key = gw.Intern("node-1", new NodeKey(100), ConceptKind.Class, """{"name":"Foo"}""");

        Assert.Equal(100, key.Value);

        // Verify the node can be read back
        using var reader = _db.CreateReader("_concepts");
        Assert.True(reader.Read());
        Assert.Equal("node-1", reader.GetString(0));     // id
        Assert.Equal(100L, reader.GetInt64(1));           // key
        Assert.Equal((int)ConceptKind.Class, (int)reader.GetInt64(2)); // kind
    }

    [Fact]
    public void Intern_ExistingKey_ReturnsSameKey()
    {
        using var gw = new GraphWriter(_writer);

        var key1 = gw.Intern("node-1", new NodeKey(100), ConceptKind.Class, """{"name":"Foo"}""");
        var key2 = gw.Intern("node-1-dup", new NodeKey(100), ConceptKind.Method, """{"name":"Bar"}""");

        Assert.Equal(key1, key2);

        // Should still have only 1 row
        int count = 0;
        using var reader = _db.CreateReader("_concepts");
        while (reader.Read()) count++;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Link_TwoConcepts_CreatesEdge()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("node-b", new NodeKey(2), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("edge-1", new NodeKey(1), new NodeKey(2), RelationKind.Contains, """{"weight":1}""");

        Assert.True(edgeRowId > 0);

        // Verify the edge can be read back
        using var reader = _db.CreateReader("_relations");
        Assert.True(reader.Read());
        Assert.Equal("edge-1", reader.GetString(0));  // id
        Assert.Equal(1L, reader.GetInt64(1));          // source_key
        Assert.Equal(2L, reader.GetInt64(2));          // target_key
        Assert.Equal((int)RelationKind.Contains, (int)reader.GetInt64(3)); // kind
    }

    [Fact]
    public void Remove_Concept_DeletesNode()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-1", new NodeKey(100), ConceptKind.File, "{}");
        bool removed = gw.Remove(new NodeKey(100));

        Assert.True(removed);

        // Should have no rows
        int count = 0;
        using var reader = _db.CreateReader("_concepts");
        while (reader.Read()) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        using var gw = new GraphWriter(_writer);

        bool removed = gw.Remove(new NodeKey(999));
        Assert.False(removed);
    }

    [Fact]
    public void Unlink_Edge_RemovesRelation()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("node-b", new NodeKey(2), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("edge-1", new NodeKey(1), new NodeKey(2), RelationKind.Calls);

        bool unlinked = gw.Unlink(edgeRowId);
        Assert.True(unlinked);

        // Should have no edge rows
        int count = 0;
        using var reader = _db.CreateReader("_relations");
        while (reader.Read()) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Intern_WithAlias_StoresAlias()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-1", new NodeKey(42), ConceptKind.GitAuthor, """{"email":"dev@test.com"}""", nodeAlias: "dev");

        using var reader = _db.CreateReader("_concepts");
        Assert.True(reader.Read());
        Assert.Equal("dev", reader.GetString(9)); // alias is column index 9
    }

    [Fact]
    public void Intern_WithTokens_StoresTokenCount()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-1", new NodeKey(42), ConceptKind.Documentation, """{"content":"hello"}""", tokens: 150);

        using var reader = _db.CreateReader("_concepts");
        Assert.True(reader.Read());
        Assert.Equal(150L, reader.GetInt64(8)); // tokens is column index 8
    }

    [Fact]
    public void Link_WithWeight_StoresWeight()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("node-b", new NodeKey(2), ConceptKind.Method, "{}");
        gw.Link("edge-1", new NodeKey(1), new NodeKey(2), RelationKind.Contains, "{}", weight: 0.75f);

        using var reader = _db.CreateReader("_relations");
        Assert.True(reader.Read());
        Assert.Equal(0.75, reader.GetDouble(8), 2); // weight is column index 8
    }

    [Fact]
    public void RoundTrip_WriteAndRead_ReturnsLinkedNodes()
    {
        using var gw = new GraphWriter(_writer);

        // Write graph: A --Contains--> B --Calls--> C
        gw.Intern("a", new NodeKey(1), ConceptKind.Project, """{"name":"Project"}""");
        gw.Intern("b", new NodeKey(2), ConceptKind.Class, """{"name":"MyClass"}""");
        gw.Intern("c", new NodeKey(3), ConceptKind.Method, """{"name":"DoWork"}""");
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);
        gw.Link("e2", new NodeKey(2), new NodeKey(3), RelationKind.Calls);

        // Read back: verify 3 nodes and 2 edges
        int nodeCount = 0;
        using (var reader = _db.CreateReader("_concepts"))
        {
            while (reader.Read()) nodeCount++;
        }

        int edgeCount = 0;
        using (var reader = _db.CreateReader("_relations"))
        {
            while (reader.Read()) edgeCount++;
        }

        Assert.Equal(3, nodeCount);
        Assert.Equal(2, edgeCount);
    }

    [Fact]
    public void Commit_IsNoOp_ForAutoCommitWriter()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("node-1", new NodeKey(1), ConceptKind.File, "{}");
        gw.Commit(); // Should not throw

        // Data should still be readable
        using var reader = _db.CreateReader("_concepts");
        Assert.True(reader.Read());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var gw = new GraphWriter(_writer);
        gw.Dispose();
        gw.Dispose(); // Should not throw
    }

    [Fact]
    public void Link_ExtendedRelationKind_StoresCorrectValue()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("author", new NodeKey(1), ConceptKind.GitAuthor, "{}");
        gw.Intern("commit", new NodeKey(2), ConceptKind.GitCommit, "{}");
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Authored);

        using var reader = _db.CreateReader("_relations");
        Assert.True(reader.Read());
        Assert.Equal((int)RelationKind.Authored, (int)reader.GetInt64(3)); // kind = 40
    }
}
