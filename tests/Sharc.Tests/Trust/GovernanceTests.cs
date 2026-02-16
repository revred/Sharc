using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class GovernanceTests
{
    [Fact]
    public void Append_ExceedsAuthorityCeiling_ShouldThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // 1. Setup Trust DB
            File.WriteAllBytes(tempFile, TrustTestFixtures.CreateTrustDatabase());
            using var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true });
            
            var registry = new AgentRegistry(db);
            var ledger = new LedgerManager(db);
            using var signer = new SharcSigner("intern-bob");

            // 2. Register Agent with limit 500
            registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer, authorityCeiling: 500));

            // 3. Attempt to spend 100 (Should Succeed)
            var validPayload = new TrustPayload(PayloadType.Financial, "Lunch Expense", 100);
            ledger.Append(validPayload, signer);

            // 4. Attempt to spend 600 (Should Fail)
            var invalidPayload = new TrustPayload(PayloadType.Financial, "Server Purchase", 600);
            
            var ex = Assert.Throws<InvalidOperationException>(() => ledger.Append(invalidPayload, signer));
            Assert.Contains("Authority ceiling exceeded", ex.Message);
            Assert.Contains("limit: 500", ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Append_StructuredPayload_ShouldBeRetrievable()
    {
        var data = TrustTestFixtures.CreateTrustDatabase();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        
        var ledger = new LedgerManager(db);
        using var signer = new SharcSigner("system-audit");
        var registry = new AgentRegistry(db);
        registry.RegisterAgent(TrustTestFixtures.CreateValidAgent(signer));

        var evidence = new EvidenceRef("Invoices", 101, new byte[] { 0xCA, 0xFE });
        var payload = new TrustPayload(PayloadType.Financial, "Audit Record", 0, new List<EvidenceRef> { evidence });

        ledger.Append(payload, signer);

        // Verify content by reading raw blob
        using var reader = db.CreateReader("_sharc_ledger");
        Assert.True(reader.Read());
        
        var blob = reader.GetBlob(3).ToArray();
        var decoded = TrustPayload.FromBytes(blob);
        
        Assert.NotNull(decoded);
        Assert.Equal(PayloadType.Financial, decoded.Type);
        Assert.Equal("Audit Record", decoded.Content);
        Assert.NotNull(decoded.Evidence);
        Assert.Single(decoded.Evidence);
        Assert.Equal("Invoices", decoded.Evidence[0].Table);
    }
}
