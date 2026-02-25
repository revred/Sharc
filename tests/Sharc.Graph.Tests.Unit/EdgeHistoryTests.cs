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

public sealed class EdgeHistoryTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;

    public EdgeHistoryTests()
    {
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);

        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, updated_at INTEGER, tokens INTEGER, alias TEXT)");
        tx.Execute("CREATE TABLE _relations (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, weight REAL)");
        tx.Execute("CREATE TABLE _relations_history (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, weight REAL, archived_at INTEGER, op TEXT)");
        tx.Commit();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Unlink_Edge_ArchivesToHistoryTable()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);

        bool unlinked = gw.Unlink(edgeRowId);
        Assert.True(unlinked);

        // Verify edge was archived to history
        using var reader = _db.CreateReader("_relations_history");
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(1)); // source_key
        Assert.Equal(2L, reader.GetInt64(2)); // target_key
        Assert.Equal((int)RelationKind.Contains, (int)reader.GetInt64(3)); // kind
        Assert.Equal("delete", reader.GetString(7)); // op
    }

    [Fact]
    public void Remove_Node_ArchivesAllEdgesToHistory()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        gw.Intern("c", new NodeKey(3), ConceptKind.Method, "{}");
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);
        gw.Link("e2", new NodeKey(1), new NodeKey(3), RelationKind.Defines);

        bool removed = gw.Remove(new NodeKey(1));
        Assert.True(removed);

        // Verify both edges were archived
        int historyCount = 0;
        using var reader = _db.CreateReader("_relations_history");
        while (reader.Read()) historyCount++;
        Assert.Equal(2, historyCount);
    }

    [Fact]
    public void Unlink_WhenHistoryTableMissing_StillDeletesEdge()
    {
        // Create a database without history table
        using var db2 = SharcDatabase.CreateInMemory();
        using var writer2 = SharcWriter.From(db2);

        using var tx = writer2.BeginTransaction();
        tx.Execute("CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, updated_at INTEGER, tokens INTEGER, alias TEXT)");
        tx.Execute("CREATE TABLE _relations (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, weight REAL)");
        tx.Commit();

        using var gw = new GraphWriter(writer2);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Calls);

        // Unlink should succeed even without history table
        bool unlinked = gw.Unlink(edgeRowId);
        Assert.True(unlinked);

        // Edge should be gone
        int edgeCount = 0;
        using var reader = db2.CreateReader("_relations");
        while (reader.Read()) edgeCount++;
        Assert.Equal(0, edgeCount);
    }

    [Fact]
    public void HistoryEntry_PreservesOriginalEdgeData()
    {
        using var gw = new GraphWriter(_writer);

        gw.Intern("a", new NodeKey(10), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(20), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("edge-42", new NodeKey(10), new NodeKey(20), RelationKind.Authored,
            """{"detail":"important"}""", weight: 0.85f);

        gw.Unlink(edgeRowId);

        using var reader = _db.CreateReader("_relations_history");
        Assert.True(reader.Read());
        Assert.Equal("edge-42", reader.GetString(0));    // id
        Assert.Equal(10L, reader.GetInt64(1));            // source_key
        Assert.Equal(20L, reader.GetInt64(2));            // target_key
        Assert.Equal((int)RelationKind.Authored, (int)reader.GetInt64(3)); // kind
        Assert.Contains("important", reader.GetString(4)); // data
        Assert.Equal(0.85, reader.GetDouble(5), 2);       // weight
        Assert.True(reader.GetInt64(6) > 0);              // archived_at (timestamp)
        Assert.Equal("delete", reader.GetString(7));       // op
    }
}
