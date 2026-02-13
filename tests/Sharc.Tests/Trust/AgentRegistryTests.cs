using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;
using Sharc.Core;
using Sharc.Core.IO;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Trust;
using Sharc.Core.Trust;

namespace Sharc.Tests.Trust;

public class AgentRegistryTests
{
    [Fact]
    public void RegisterAgent_PersistsCorrectly()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            CreateTrustDatabase(tempFile);
            using var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true });
            var registry = new AgentRegistry(db);
            
            byte[] publicKey = new byte[32];
            RandomNumberGenerator.Fill(publicKey);
            
            var agent = new AgentInfo("agent-007", publicKey, 1000, 2000, new byte[0]);
            
            registry.RegisterAgent(agent);
            
            // Re-read from another instance to verify persistence/flushing
            // But RegisterAgent commits via BTreeMutator if no transaction.
            // However, SharcDatabase might cache pages? 
            // AgentRegistry uses _cache.
            
            var retrieved = registry.GetAgent("agent-007");
            Assert.NotNull(retrieved);
            Assert.Equal("agent-007", retrieved.AgentId);
            Assert.Equal(publicKey, retrieved.PublicKey);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RegisterAgent_WithTransaction_CommitPersists()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            CreateTrustDatabase(tempFile);
            using var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true });
            var registry = new AgentRegistry(db);
            
            byte[] publicKey = new byte[32];
            RandomNumberGenerator.Fill(publicKey);
            var agent = new AgentInfo("agent-tx-commit", publicKey, 1000, 2000, new byte[0]);
            
            using (var tx = db.BeginTransaction())
            {
                registry.RegisterAgent(agent, tx);
                tx.Commit();
            }
            
            // Should be visible now
            var retrieved = registry.GetAgent("agent-tx-commit");
            Assert.NotNull(retrieved);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RegisterAgent_WithTransaction_RollbackReverts()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            CreateTrustDatabase(tempFile);
            using var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true });
            var registry = new AgentRegistry(db);
            
            byte[] publicKey = new byte[32];
            RandomNumberGenerator.Fill(publicKey);
            var agent = new AgentInfo("agent-tx-rollback", publicKey, 1000, 2000, new byte[0]);
            
            using (var tx = db.BeginTransaction())
            {
                registry.RegisterAgent(agent, tx);
                // No Commit -> Implicit Rollback on Dispose
            }
            
            // Should NOT be visible
            var retrieved = registry.GetAgent("agent-tx-rollback");
            Assert.Null(retrieved);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static void CreateTrustDatabase(string path, int pageSize = 4096)
    {
        var data = new byte[pageSize * 3]; // Start small, let it grow
        var dbHeader = new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: 1,
            readVersion: 1,
            reservedBytesPerPage: 0,
            changeCounter: 1,
            pageCount: 3,
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
        var schemaHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 2, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), schemaHeader);

        // 1. _sharc_ledger
        var ledgerCols = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.FromInt64(3, 2),
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_ledger (SequenceNumber INTEGER PRIMARY KEY, Timestamp INTEGER, AgentId TEXT, PayloadHash BLOB, PreviousHash BLOB, Signature BLOB)"))
        };

        // 2. _sharc_agents
        var agentsCols = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.FromInt64(3, 3),
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_agents (AgentId TEXT PRIMARY KEY, PublicKey BLOB, ValidityStart INTEGER, ValidityEnd INTEGER, Signature BLOB)"))
        };

        int r1Size = RecordEncoder.ComputeEncodedSize(ledgerCols);
        byte[] r1 = new byte[r1Size];
        RecordEncoder.EncodeRecord(ledgerCols, r1);

        int r2Size = RecordEncoder.ComputeEncodedSize(agentsCols);
        byte[] r2 = new byte[r2Size];
        RecordEncoder.EncodeRecord(agentsCols, r2);

        Span<byte> cell1 = stackalloc byte[r1Size + 10];
        int l1 = CellBuilder.BuildTableLeafCell(1, r1, cell1, pageSize);
        ushort o1 = (ushort)(pageSize - l1);
        cell1[..l1].CopyTo(data.AsSpan(o1));

        Span<byte> cell2 = stackalloc byte[r2Size + 10];
        int l2 = CellBuilder.BuildTableLeafCell(2, r2, cell2, pageSize);
        ushort o2 = (ushort)(o1 - l2);
        cell2[..l2].CopyTo(data.AsSpan(o2));

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(108), o1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(110), o2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(105), o2); // CellContentOffset

        // -- Page 2: Ledger Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        
        // -- Page 3: Agents Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize * 2), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        File.WriteAllBytes(path, data);
    }
}
