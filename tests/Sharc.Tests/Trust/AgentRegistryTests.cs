// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Xunit;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Trust;

namespace Sharc.Tests.Trust;

public class AgentRegistryTests
{
    [Fact]
    public void RegisterAgent_PersistsCorrectly()
    {
        var data = CreateTrustDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var registry = new AgentRegistry(db);

        var signer = new SharcSigner("agent-007");
        var agent = TrustTestFixtures.CreateValidAgent(signer);

        registry.RegisterAgent(agent);

        var retrieved = registry.GetAgent("agent-007");
        Assert.NotNull(retrieved);
        Assert.Equal("agent-007", retrieved.AgentId);
        Assert.Equal(signer.GetPublicKey(), retrieved.PublicKey);
    }

    [Fact]
    public void RegisterAgent_WithTransaction_CommitPersists()
    {
        var data = CreateTrustDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var registry = new AgentRegistry(db);

        var signer = new SharcSigner("agent-tx-commit");
        var agent = TrustTestFixtures.CreateValidAgent(signer);

        using (var tx = db.BeginTransaction())
        {
            registry.RegisterAgent(agent, tx);
            tx.Commit();
        }

        // Should be visible now
        var retrieved = registry.GetAgent("agent-tx-commit");
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void RegisterAgent_WithTransaction_RollbackReverts()
    {
        var data = CreateTrustDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var registry = new AgentRegistry(db);

        var signer = new SharcSigner("agent-tx-rollback");
        var agent = TrustTestFixtures.CreateValidAgent(signer);

        using (var tx = db.BeginTransaction())
        {
            registry.RegisterAgent(agent, tx);
            // No Commit -> Implicit Rollback on Dispose
        }

        // Should NOT be visible
        var retrieved = registry.GetAgent("agent-tx-rollback");
        Assert.Null(retrieved);
    }

    private static byte[] CreateTrustDatabaseBytes(int pageSize = 4096)
    {
        var data = new byte[pageSize * 4];
        var dbHeader = new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: 1,
            readVersion: 1,
            reservedBytesPerPage: 0,
            changeCounter: 1,
            pageCount: 4,
            firstFreelistPage: 0,
            freelistPageCount: 0,
            schemaCookie: 1,
            schemaFormat: 4,
            textEncoding: 1,
            userVersion: 0,
            applicationId: 0,
            sqliteVersionNumber: 3042000
        );
        DatabaseHeader.Write(data, dbHeader);

        // -- Page 1: Schema Leaf --
        var schemaHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 3, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), schemaHeader);

        // 1. _sharc_ledger
        var ledgerCols = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.FromInt64(3, 2),
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_ledger (SequenceNumber INTEGER PRIMARY KEY, Timestamp INTEGER, AgentId TEXT, Payload BLOB, PayloadHash BLOB, PreviousHash BLOB, Signature BLOB)"))
        };

        // 2. _sharc_agents
        var agentsCols = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.FromInt64(3, 3),
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_agents (AgentId TEXT PRIMARY KEY, Class INTEGER, PublicKey BLOB, AuthorityCeiling INTEGER, WriteScope TEXT, ReadScope TEXT, ValidityStart INTEGER, ValidityEnd INTEGER, ParentAgent TEXT, CoSignRequired INTEGER, Signature BLOB)"))
        };

        // 3. _sharc_scores
        var scoresCols = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_scores")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_scores")),
            ColumnValue.FromInt64(3, 4),
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_scores (AgentId TEXT PRIMARY KEY, Score REAL, Confidence REAL, LastUpdated INTEGER, LastRatingCount INTEGER)"))
        };

        int r1Size = RecordEncoder.ComputeEncodedSize(ledgerCols);
        byte[] r1 = new byte[r1Size];
        RecordEncoder.EncodeRecord(ledgerCols, r1);

        int r2Size = RecordEncoder.ComputeEncodedSize(agentsCols);
        byte[] r2 = new byte[r2Size];
        RecordEncoder.EncodeRecord(agentsCols, r2);

        int r3Size = RecordEncoder.ComputeEncodedSize(scoresCols);
        byte[] r3 = new byte[r3Size];
        RecordEncoder.EncodeRecord(scoresCols, r3);

        Span<byte> cell1 = stackalloc byte[r1Size + 10];
        int l1 = CellBuilder.BuildTableLeafCell(1, r1, cell1, pageSize);
        ushort o1 = (ushort)(pageSize - l1);
        cell1[..l1].CopyTo(data.AsSpan(o1));

        Span<byte> cell2 = stackalloc byte[r2Size + 10];
        int l2 = CellBuilder.BuildTableLeafCell(2, r2, cell2, pageSize);
        ushort o2 = (ushort)(o1 - l2);
        cell2[..l2].CopyTo(data.AsSpan(o2));

        Span<byte> cell3 = stackalloc byte[r3Size + 10];
        int l3 = CellBuilder.BuildTableLeafCell(3, r3, cell3, pageSize);
        ushort o3 = (ushort)(o2 - l3);
        cell3[..l3].CopyTo(data.AsSpan(o3));

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(108), o1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(110), o2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(112), o3);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(105), o3); // CellContentOffset

        // -- Page 2: Ledger Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        
        // -- Page 3: Agents Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize * 2), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        // -- Page 4: Scores Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize * 3), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        return data;
    }
}
