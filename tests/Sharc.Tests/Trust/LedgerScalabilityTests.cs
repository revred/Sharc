using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class LedgerScalabilityTests
{
    [Fact]
    public void Append_HundredsOfEntries_ShouldSucceed()
    {
        var data = TrustTestFixtures.CreateTrustDatabase();
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var ledger = new LedgerManager(db);
        using var signer = new SharcSigner("test-agent");

        // Register agent so VerifyIntegrity can look up the public key
        var registry = new AgentRegistry(db);

        var agent = TrustTestFixtures.CreateValidAgent(
            signer,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
            authorityCeiling: 1000000);

        registry.RegisterAgent(agent);

        // Append enough entries to force page splits
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
                throw new InvalidOperationException($"Failed at entry {i + 1}: {ex.Message}", ex);
            }
        }

        // Verify integrity manually to find the break
        var deltas = ledger.ExportDeltas(1);

        byte[] expectedPrevHash = new byte[32];
        for (int i = 0; i < deltas.Count; i++)
        {
            var decoded = db.RecordDecoder.DecodeRecord(deltas[i]);
            long seq = decoded[0].AsInt64();
            byte[] payloadHash = decoded[4].AsBytes().ToArray();
            byte[] prevHash = decoded[5].AsBytes().ToArray();

            if (seq != i + 1)
                throw new InvalidOperationException($"Sequence mismatch at index {i}. Expected {i + 1}, got {seq}");

            if (!prevHash.AsSpan().SequenceEqual(expectedPrevHash))
                throw new InvalidOperationException($"Hash chain break at index {i} (Seq {seq})");

            expectedPrevHash = payloadHash;
        }

        Assert.Equal(entryCount, deltas.Count);
        Assert.True(ledger.VerifyIntegrity(), "Ledger integrity check failed.");
    }
}
