using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Core.IO;
using Sharc.Trust;
using Sharc.Core.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class LedgerScalabilityTests
{
    [Fact]
    public void Append_HundredsOfEntries_ShouldSucceed()
    {
        string tempFile = Path.GetTempFileName();
        try 
        {
            // 1. Setup DB
            CreateTrustDatabase(tempFile);
            using var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true });
            
            var ledger = new LedgerManager(db);
            using var signer = new SharcSigner("test-agent");
            
            // Register agent so VerifyIntegrity can look up the public key
            var registry = new AgentRegistry(db);
            registry.RegisterAgent(new AgentInfo(
                signer.AgentId, 
                signer.GetPublicKey(),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
                new byte[0] 
            ));
    
            // 2. Append enough entries to force page splits
            // A standard 4096-byte page holds ~50-60 entries.
            // 500 entries should force multiple splits and depth increase.
            int entryCount = 500;
            
            for (int i = 0; i < entryCount; i++)
            {
                try 
                {
                    ledger.Append($"context-payload-{i}", signer);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed at entry {i+1}: {ex.Message}", ex);
                }
            }
    
            // 3. Verify integrity manually to find the break
            var deltas = ledger.ExportDeltas(1);


            byte[] expectedPrevHash = new byte[32];
            for (int i = 0; i < deltas.Count; i++)
            {
                var decoded = db.RecordDecoder.DecodeRecord(deltas[i]);
                long seq = decoded[0].AsInt64();
                byte[] payloadHash = decoded[3].AsBytes().ToArray();
                byte[] prevHash = decoded[4].AsBytes().ToArray();
                
                if (seq != i + 1)
                     throw new Exception($"Sequence mismatch at index {i}. Expected {i+1}, got {seq}");
                
                if (!prevHash.AsSpan().SequenceEqual(expectedPrevHash))
                     throw new Exception($"Hash chain break at index {i} (Seq {seq})");

                expectedPrevHash = payloadHash;
            }
            
            Assert.Equal(entryCount, deltas.Count);

            Assert.True(ledger.VerifyIntegrity(), "Ledger integrity check failed.");
    
    
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
