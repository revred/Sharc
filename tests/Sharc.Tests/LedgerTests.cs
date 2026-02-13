using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests;

public class LedgerTests
{
    [Fact]
    public void SharcSigner_SignAndVerify_Works()
    {
        var signer = new SharcSigner("test-agent");
        var data = new byte[] { 1, 2, 3, 4 };
        var sig = signer.Sign(data);
        var pub = signer.GetPublicKey();
        
        Assert.True(signer.Verify(data, sig));
        Assert.True(SharcSigner.Verify(data, sig, pub));
        
        data[0] ^= 0xFF;
        Assert.False(SharcSigner.Verify(data, sig, pub));
    }

    private static byte[] CreateLedgerDatabase(int pageSize = 4096)
    {
        var data = new byte[pageSize * 2]; // 2 pages: 1 for schema, 1 for ledger
        var dbHeader = new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: 1,
            readVersion: 1,
            reservedBytesPerPage: 0,
            changeCounter: 1,
            pageCount: 2,
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
        // Offset 100 is B-tree header
        var schemaHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 1, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), schemaHeader);

        // Create schema cell for _sharc_ledger
        // sqlite_schema columns: type, name, tbl_name, rootpage, sql
        var schemaColumns = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.FromInt64(3, 2), // Root page 2
            ColumnValue.Text(4, System.Text.Encoding.UTF8.GetBytes("CREATE TABLE _sharc_ledger (SequenceNumber INTEGER PRIMARY KEY, Timestamp INTEGER, AgentId TEXT, PayloadHash BLOB, PreviousHash BLOB, Signature BLOB)"))
        };

        int schemaRecordSize = RecordEncoder.ComputeEncodedSize(schemaColumns);
        byte[] schemaRecord = new byte[schemaRecordSize];
        RecordEncoder.EncodeRecord(schemaColumns, schemaRecord);

        // Build leaf cell (payloadSize varint + rowid varint + payload)
        Span<byte> schemaCell = stackalloc byte[schemaRecordSize + 10];
        int cellLen = CellBuilder.BuildTableLeafCell(1, schemaRecord, schemaCell, pageSize);

        // Write cell at end of page 1
        ushort cellOffset = (ushort)(pageSize - cellLen);
        schemaCell[..cellLen].CopyTo(data.AsSpan(cellOffset));

        // Update cell pointer in Page 1 (offset 100 + 8 = 108)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(108), cellOffset);
        
        // Update Page 1 Header ContentOffset
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(100 + 5), cellOffset);

        // -- Page 2: Ledger Leaf (Empty) --
        var ledgerHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(pageSize), ledgerHeader);

        return data;
    }

    [Fact]
    public void Ledger_AppendAndVerify_ChainIntegrityMaintained()
    {
        var data = CreateLedgerDatabase();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        
        var ledger = new LedgerManager(db);
        var signer = new SharcSigner("agent-007");

        // 1. Append first entry
        ledger.Append("Initial trust context established.", signer);

        // 2. Append second entry
        ledger.Append("Action verified by agent.", signer);

        // 3. Verify integrity — pass the signer's public key for verification
        var signers = new Dictionary<string, byte[]> { [signer.AgentId] = signer.GetPublicKey() };
        Assert.True(ledger.VerifyIntegrity(signers));

        // 4. Verify content
        using var reader = db.CreateReader("_sharc_ledger");
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0)); // Seq
        Assert.Equal("agent-007", reader.GetString(2));

        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt64(0)); // Seq
        
        // Check hash link: entry 2 prevHash == entry 1 payloadHash
        using var reader2 = db.CreateReader("_sharc_ledger");
        Assert.True(reader2.Read());
        byte[] hash1 = reader2.GetBlob(3).ToArray();
        Assert.True(reader2.Read());
        byte[] prevHash2 = reader2.GetBlob(4).ToArray();
        Assert.Equal(hash1, prevHash2);
    }

    [Fact]
    public void Ledger_TamperAttempt_DetectedByVerifyIntegrity()
    {
        var data = CreateLedgerDatabase();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        
        var ledger = new LedgerManager(db);
        var signer = new SharcSigner("agent-alpha");

        ledger.Append("Valid data 1", signer);
        ledger.Append("Valid data 2", signer);

        var keys = new Dictionary<string, byte[]> { { signer.AgentId, signer.GetPublicKey() } };
        Assert.True(ledger.VerifyIntegrity(keys));

        // TAMPER: Manually flip a bit in the first entry's Payload Hash
        // We read via the page source to bypass/interact correctly with caching
        var page2 = new byte[4096];
        db.PageSource.ReadPage(2, page2);
        
        ushort cellPtr = BinaryPrimitives.ReadUInt16BigEndian(page2.AsSpan(8, 2)); 
        // Flip something in the record body
        page2[cellPtr + 30] ^= 0xFF; 

        // Write the corrupted page back to simulated storage
        // Since it's OpenMemory, we might need a way to force-refresh or just trust the next read.
        // Actually, db.PageSource.WritePage on a ProxyPageSource might not be enough if there's a Pager cache.
        // But Sharc doesn't have a separate Pager cache in the way SQLite does yet, it uses the PageSource.
        // Wait, I'll check if SharcDatabase has a Pager. 
        // If I use tx.WritePage it will be buffered.
        // If I want to simulate OUT-OF-BAND corruption, I should modify the 'data' array AND clear any cache.
        // For simplicity in OpenMemory, I'll just close and re-open the database after tampering with 'data'.
        
        db.Dispose();
        data[4096 + cellPtr + 30] ^= 0xFF; // Tamper the raw array
        
        using var db2 = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var ledger2 = new LedgerManager(db2);
        
        // Now integrity should fail
        Assert.False(ledger2.VerifyIntegrity(keys));
    }

    private static byte[] CreateTrustDatabase(int pageSize = 4096)
    {
        var data = new byte[pageSize * 3]; // 3 pages: 1 schema, 1 ledger, 1 agents
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
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(105), o2);

        // -- Page 2: Ledger Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        
        // -- Page 3: Agents Leaf (Empty) --
        BTreePageHeader.Write(data.AsSpan(pageSize * 2), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        return data;
    }

    [Fact]
    public void AgentRegistry_CanRegisterAndRetrieveAgent()
    {
        var data = CreateTrustDatabase();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        
        var registry = new AgentRegistry(db);
        var pubKey = new byte[] { 0xAA, 0xBB, 0xCC };
        var sig = new byte[] { 0x11, 0x22 };
        var agent = new AgentInfo("agent-1", pubKey, 1000, 2000, sig);

        registry.RegisterAgent(agent);
        
        var retrieved = registry.GetAgent("agent-1");
        Assert.NotNull(retrieved);
        Assert.Equal("agent-1", retrieved.AgentId);
        Assert.Equal(pubKey, retrieved.PublicKey);
    }

    [Fact]
    public void DistributedTrust_Sync_MultipleAgents_Works()
    {
        // 1. Setup Source DB (A)
        var dataA = CreateTrustDatabase();
        using var dbA = SharcDatabase.OpenMemory(dataA, new SharcOpenOptions { Writable = true });
        var ledgerA = new LedgerManager(dbA);
        var registryA = new AgentRegistry(dbA);
        
        var alice = new SharcSigner("alice");
        var bob = new SharcSigner("bob");

        // Register agents in A
        registryA.RegisterAgent(new AgentInfo("alice", alice.GetPublicKey(), 0, 0, Array.Empty<byte>()));
        registryA.RegisterAgent(new AgentInfo("bob", bob.GetPublicKey(), 0, 0, Array.Empty<byte>()));

        // Alice and Bob append to A
        ledgerA.Append("Alice's first thought", alice);
        ledgerA.Append("Bob's rebuttal", bob);
        ledgerA.Append("Alice's final word", alice);

        Assert.True(ledgerA.VerifyIntegrity());

        // 2. Setup Destination DB (B)
        var dataB = CreateTrustDatabase();
        using var dbB = SharcDatabase.OpenMemory(dataB, new SharcOpenOptions { Writable = true });
        var ledgerB = new LedgerManager(dbB);
        var registryB = new AgentRegistry(dbB);

        // We MUST register agents in B too for verification to pass during import
        registryB.RegisterAgent(new AgentInfo("alice", alice.GetPublicKey(), 0, 0, Array.Empty<byte>()));
        registryB.RegisterAgent(new AgentInfo("bob", bob.GetPublicKey(), 0, 0, Array.Empty<byte>()));

        // 3. Export from A, Import into B
        var deltas = ledgerA.ExportDeltas(1);
        Assert.Equal(3, deltas.Count);
        
        ledgerB.ImportDeltas(deltas);

        // 4. Verify B
        Assert.True(ledgerB.VerifyIntegrity());
        
        using var readerB = dbB.CreateReader("_sharc_ledger");
        Assert.True(readerB.Read()); Assert.Equal("alice", readerB.GetString(2));
        Assert.True(readerB.Read()); Assert.Equal("bob", readerB.GetString(2));
        Assert.True(readerB.Read()); Assert.Equal("alice", readerB.GetString(2));
    }
}
