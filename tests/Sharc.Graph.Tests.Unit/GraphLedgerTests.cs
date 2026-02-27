// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
using Sharc.Trust;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public sealed class GraphLedgerTests : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;
    private readonly LedgerManager _ledger;
    private readonly SharcSigner _signer;

    public GraphLedgerTests()
    {
        // CreateInMemory() includes _sharc_ledger, _sharc_agents, _sharc_scores, _sharc_audit
        _db = SharcDatabase.CreateInMemory();
        _writer = SharcWriter.From(_db);
        _ledger = new LedgerManager(_db);
        _signer = new SharcSigner("test-graph-agent");

        // Add graph tables
        using var tx = _writer.BeginTransaction();
        tx.Execute("CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, updated_at INTEGER, tokens INTEGER, alias TEXT)");
        tx.Execute("CREATE TABLE _relations (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, cvn INTEGER, lvn INTEGER, sync_status INTEGER, weight REAL)");
        tx.Execute("CREATE TABLE _relations_history (id TEXT, source_key INTEGER, target_key INTEGER, kind INTEGER, data TEXT, weight REAL, archived_at INTEGER, op TEXT)");
        tx.Commit();
    }

    public void Dispose()
    {
        _signer.Dispose();
        _writer.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Link_WithLedger_AppendsProvenanceEntry()
    {
        using var gw = new GraphWriter(_writer, ledger: _ledger, signer: _signer);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);

        // Verify ledger has entries (Intern x2 + Link x1 = 3 entries)
        int entryCount = CountLedgerEntries();
        Assert.Equal(3, entryCount);
    }

    [Fact]
    public void Unlink_WithLedger_AppendsProvenanceEntry()
    {
        using var gw = new GraphWriter(_writer, ledger: _ledger, signer: _signer);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        long edgeRowId = gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Calls);

        int beforeCount = CountLedgerEntries();
        gw.Unlink(edgeRowId);
        int afterCount = CountLedgerEntries();

        Assert.Equal(beforeCount + 1, afterCount);
    }

    [Fact]
    public void Link_WithoutLedger_NoLedgerEntries()
    {
        // Default constructor — no ledger
        using var gw = new GraphWriter(_writer);

        gw.Intern("a", new NodeKey(1), ConceptKind.Class, "{}");
        gw.Intern("b", new NodeKey(2), ConceptKind.Method, "{}");
        gw.Link("e1", new NodeKey(1), new NodeKey(2), RelationKind.Contains);

        int entryCount = CountLedgerEntries();
        Assert.Equal(0, entryCount);
    }

    [Fact]
    public void Intern_WithLedger_AppendsProvenanceEntry()
    {
        using var gw = new GraphWriter(_writer, ledger: _ledger, signer: _signer);

        gw.Intern("node-1", new NodeKey(42), ConceptKind.GitCommit, """{"sha":"abc"}""");

        int entryCount = CountLedgerEntries();
        Assert.Equal(1, entryCount);
    }

    private int CountLedgerEntries()
    {
        int count = 0;
        using var reader = _db.CreateReader("_sharc_ledger");
        while (reader.Read()) count++;
        return count;
    }
}