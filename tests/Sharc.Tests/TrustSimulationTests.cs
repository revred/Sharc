using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;
using Xunit.Abstractions;

namespace Sharc.Tests;

public class TrustSimulationTests
{
    private readonly ITestOutputHelper _output;

    public TrustSimulationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MultiBot_SecuritySimulation_RejectsMaliciousInjection()
    {
        _output.WriteLine("=== STARTING MULTI-BOT TRUST SIMULATION ===");

        // 1. SETUP: Three Bots (Alice, Bob, Mallory)
        var alice = new SharcSigner("alice-oncology");
        var bob = new SharcSigner("bob-genomics");
        var mallory = new SharcSigner("mallory-malicious");

        // 2. SETUP: Alice's Knowledge Base (Database A)
        var dataA = CreateEmptyTrustDatabase();
        using var dbA = SharcDatabase.OpenMemory(dataA, new SharcOpenOptions { Writable = true });
        var ledgerA = new LedgerManager(dbA);
        var registryA = new AgentRegistry(dbA);

        // Register trusted agents in A
        registryA.RegisterAgent(new AgentInfo("alice-oncology", alice.GetPublicKey(), 0, 0, Array.Empty<byte>()));
        registryA.RegisterAgent(new AgentInfo("bob-genomics", bob.GetPublicKey(), 0, 0, Array.Empty<byte>()));

        // Alice records initial ground truth
        _output.WriteLine("[Alice] Recording clinical observation...");
        ledgerA.Append("Observation: Patient cohort show positive response to Inhibitor-X.", alice);
        
        // 3. SETUP: Bob's Knowledge Base (Database B)
        var dataB = CreateEmptyTrustDatabase();
        using var dbB = SharcDatabase.OpenMemory(dataB, new SharcOpenOptions { Writable = true });
        var ledgerB = new LedgerManager(dbB);
        var registryB = new AgentRegistry(dbB);

        // Bob trusts Alice and himself, but does NOT know Mallory
        registryB.RegisterAgent(new AgentInfo("alice-oncology", alice.GetPublicKey(), 0, 0, Array.Empty<byte>()));
        registryB.RegisterAgent(new AgentInfo("bob-genomics", bob.GetPublicKey(), 0, 0, Array.Empty<byte>()));

        // 4. SCENARIO: Normal Synchronization
        _output.WriteLine("[System] Alice exports deltas to Bob...");
        var aliceDeltas = ledgerA.ExportDeltas(1);
        
        _output.WriteLine("[Bob] Importing Alice's deltas...");
        ledgerB.ImportDeltas(aliceDeltas);
        Assert.True(ledgerB.VerifyIntegrity());
        _output.WriteLine("[Bob] Integrity Verified. Knowledge synchronized.");

        // 5. ATTACK SCENARIO A: Malicious Injection (Unknown Agent)
        _output.WriteLine("\n=== ATTACK A: Injection from Unknown Agent (Mallory) ===");
        
        // Mallory creates her own database to generate "legal-looking" records
        var dataM = CreateEmptyTrustDatabase();
        using var dbM = SharcDatabase.OpenMemory(dataM, new SharcOpenOptions { Writable = true });
        var ledgerM = new LedgerManager(dbM);
        
        // Mallory forces a sequence number to look like it follows Alice's last entry (seq 2)
        ledgerM.Append("MALICIOUS: Inhibitor-X causes severe side effects.", mallory);
        var malloryDeltas = ledgerM.ExportDeltas(1);

        _output.WriteLine("[Bob] Bob receives delta from Mallory...");
        var ex = Assert.Throws<InvalidOperationException>(() => ledgerB.ImportDeltas(malloryDeltas));
        _output.WriteLine($"[Bob] REJECTED: {ex.Message} (Agent not in local registry)");

        // 6. ATTACK SCENARIO B: Spoofing (Mallory uses Alice's ID but her own key)
        _output.WriteLine("\n=== ATTACK B: Identity Spoofing (Mallory claims to be Alice) ===");
        
        // We simulate Mallory creating a record that claims to be from 'alice-oncology' but is signed by Mallory's key
        // To do this, we'll manually construct a record
        var maliciousColumns = new[]
        {
            ColumnValue.FromInt64(1, 2), // Sequence 2
            ColumnValue.FromInt64(2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ColumnValue.Text(2, System.Text.Encoding.UTF8.GetBytes("alice-oncology")), // Claim identity
            ColumnValue.Blob(2, new byte[32]), // Fake payload hash
            ColumnValue.Blob(2, aliceDeltas[0].AsSpan().Slice(aliceDeltas[0].Length - 32).ToArray()), // Link to Alice's last hash (fake)
            ColumnValue.Blob(2, mallory.Sign(new byte[64])) // Signed by Mallory's key
        };
        
        int recordSize = RecordEncoder.ComputeEncodedSize(maliciousColumns);
        byte[] maliciousRecord = new byte[recordSize];
        RecordEncoder.EncodeRecord(maliciousColumns, maliciousRecord);

        _output.WriteLine("[Bob] Bob receives spoofed delta claiming to be from Alice...");
        ex = Assert.Throws<InvalidOperationException>(() => ledgerB.ImportDeltas(new[] { maliciousRecord }));
        _output.WriteLine($"[Bob] REJECTED: {ex.Message} (Cryptographic signature mismatch)");

        // 7. ATTACK SCENARIO C: Tampered History (Mallory tries to rewrite Alice's seq 1)
        _output.WriteLine("\n=== ATTACK C: History Rewrite (Mallory tries to change initial fact) ===");
        
        byte[] tamperedAliceDelta = aliceDeltas[0].ToArray();
        tamperedAliceDelta[tamperedAliceDelta.Length - 100] ^= 0xFF; // Flip a bit in the record

        _output.WriteLine("[Bob] Bob receives tampered version of Alice's history...");
        // Bob already has Seq 1, so he should reject a duplicate sequence unless handling forks (which Bob's MVP rejects)
        ex = Assert.Throws<InvalidOperationException>(() => ledgerB.ImportDeltas(new[] { tamperedAliceDelta }));
        _output.WriteLine($"[Bob] REJECTED: {ex.Message} (Sequence conflict)");

        _output.WriteLine("\n=== SIMULATION COMPLETE: ALL ATTACKS MITIGATED ===");
    }

    private static byte[] CreateEmptyTrustDatabase(int pageSize = 4096)
    {
        var data = new byte[pageSize * 3];
        var dbHeader = new DatabaseHeader(pageSize: pageSize, writeVersion: 1, readVersion: 1, reservedBytesPerPage: 0, changeCounter: 1, pageCount: 3, firstFreelistPage: 0, freelistPageCount: 0, schemaCookie: 1, schemaFormat: 4, textEncoding: 1, userVersion: 0, applicationId: 0, sqliteVersionNumber: 3042000);
        DatabaseHeader.Write(data, dbHeader);

        var schemaHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 2, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), schemaHeader);

        // _sharc_ledger
        var ledgerCols = new[] { ColumnValue.Text(0, "table"u8.ToArray()), ColumnValue.Text(0, "_sharc_ledger"u8.ToArray()), ColumnValue.Text(0, "_sharc_ledger"u8.ToArray()), ColumnValue.FromInt64(0, 2), ColumnValue.Text(0, "CREATE TABLE _sharc_ledger (SequenceNumber INTEGER PRIMARY KEY, Timestamp INTEGER, AgentId TEXT, PayloadHash BLOB, PreviousHash BLOB, Signature BLOB)"u8.ToArray()) };
        // _sharc_agents
        var agentsCols = new[] { ColumnValue.Text(0, "table"u8.ToArray()), ColumnValue.Text(0, "_sharc_agents"u8.ToArray()), ColumnValue.Text(0, "_sharc_agents"u8.ToArray()), ColumnValue.FromInt64(0, 3), ColumnValue.Text(0, "CREATE TABLE _sharc_agents (AgentId TEXT PRIMARY KEY, PublicKey BLOB, ValidityStart INTEGER, ValidityEnd INTEGER, Signature BLOB)"u8.ToArray()) };

        int r1Size = RecordEncoder.ComputeEncodedSize(ledgerCols);
        byte[] r1 = new byte[r1Size]; RecordEncoder.EncodeRecord(ledgerCols, r1);
        int r2Size = RecordEncoder.ComputeEncodedSize(agentsCols);
        byte[] r2 = new byte[r2Size]; RecordEncoder.EncodeRecord(agentsCols, r2);

        Span<byte> c1 = stackalloc byte[r1Size + 10]; int l1 = CellBuilder.BuildTableLeafCell(1, r1, c1, pageSize);
        ushort o1 = (ushort)(pageSize - l1); c1[..l1].CopyTo(data.AsSpan(o1));
        Span<byte> c2 = stackalloc byte[r2Size + 10]; int l2 = CellBuilder.BuildTableLeafCell(2, r2, c2, pageSize);
        ushort o2 = (ushort)(o1 - l2); c2[..l2].CopyTo(data.AsSpan(o2));

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(108), o1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(110), o2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(100 + 5), o2);

        BTreePageHeader.Write(data.AsSpan(pageSize), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        BTreePageHeader.Write(data.AsSpan(pageSize * 2), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        return data;
    }
}
