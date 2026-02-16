using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Trust;
using Xunit;
using Xunit.Abstractions;
using Sharc.Tests.Trust;

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
        using var alice = new SharcSigner("alice-oncology");
        using var bob = new SharcSigner("bob-genomics");
        using var mallory = new SharcSigner("mallory-malicious");

        // 2. SETUP: Alice's Knowledge Base (Database A)
        var dataA = TrustTestFixtures.CreateTrustDatabase();
        using var dbA = SharcDatabase.OpenMemory(dataA, new SharcOpenOptions { Writable = true });
        var ledgerA = new LedgerManager(dbA);
        var registryA = new AgentRegistry(dbA);

        // Register trusted agents in A
        registryA.RegisterAgent(TrustTestFixtures.CreateValidAgent(alice));
        registryA.RegisterAgent(TrustTestFixtures.CreateValidAgent(bob));

        // Alice records initial ground truth
        _output.WriteLine("[Alice] Recording clinical observation...");
        ledgerA.Append("Observation: Patient cohort show positive response to Inhibitor-X.", alice);
        
        // 3. SETUP: Bob's Knowledge Base (Database B)
        var dataB = TrustTestFixtures.CreateTrustDatabase();
        using var dbB = SharcDatabase.OpenMemory(dataB, new SharcOpenOptions { Writable = true });
        var ledgerB = new LedgerManager(dbB);
        var registryB = new AgentRegistry(dbB);

        // Bob trusts Alice and himself, but does NOT know Mallory
        registryB.RegisterAgent(TrustTestFixtures.CreateValidAgent(alice));
        registryB.RegisterAgent(TrustTestFixtures.CreateValidAgent(bob));

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
        var dataM = TrustTestFixtures.CreateTrustDatabase();
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
            ColumnValue.Blob(2, Array.Empty<byte>()), // Empty Payload
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

}
