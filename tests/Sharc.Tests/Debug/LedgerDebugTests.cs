using Sharc.Core.Storage;
using Sharc.Trust;
using Sharc.Core.Trust;
using Sharc;
using Xunit;

namespace Sharc.Tests.Debug;

public class LedgerDebugTests : IDisposable
{
    private string _dbPath;
    private SharcDatabase _db;
    private LedgerManager _ledger;
    private SharcSigner _signer;

    public LedgerDebugTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shard_ledger_debug_{Guid.NewGuid()}.db");

        // Create DB and Table manually
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA page_size = 4096;
                CREATE TABLE _sharc_ledger (
                    seq INTEGER PRIMARY KEY,
                    ts INTEGER,
                    agent TEXT,
                    payload BLOB,
                    hash BLOB,
                    prev_hash BLOB,
                    sig BLOB
                );
                CREATE TABLE _sharc_agents (
                    id TEXT PRIMARY KEY,
                    cls INTEGER,
                    pub_key BLOB,
                    auth_ceil INTEGER,
                    w_scope TEXT,
                    r_scope TEXT,
                    val_start INTEGER,
                    val_end INTEGER,
                    parent TEXT,
                    cosign INTEGER,
                    sig BLOB
                );
            ";
            cmd.ExecuteNonQuery();
        }

        _db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        
        _signer = new SharcSigner("debug-agent");
        
        // Register Agent
        var registry = new AgentRegistry(_db);
        var pubKey = _signer.GetPublicKey();
        
        // Create pre-signed info to calculate signature
        var unsignedInfo = new AgentInfo(
            "debug-agent",
            AgentClass.User,
            pubKey,
            1000000, // Ceiling
            "*", "*", // Scopes
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
            "", // Parent
            false, // CoSign
            Array.Empty<byte>() // Sig placeholder
        );
        
        var verifyBuf = AgentRegistry.GetVerificationBuffer(unsignedInfo);
        var sig = _signer.Sign(verifyBuf);
        
        var agentInfo = new AgentInfo(
            unsignedInfo.AgentId,
            unsignedInfo.Class,
            unsignedInfo.PublicKey,
            unsignedInfo.AuthorityCeiling,
            unsignedInfo.WriteScope,
            unsignedInfo.ReadScope,
            unsignedInfo.ValidityStart,
            unsignedInfo.ValidityEnd,
            unsignedInfo.ParentAgent,
            unsignedInfo.CoSignRequired,
            sig
        );
        
        registry.RegisterAgent(agentInfo);

        _ledger = new LedgerManager(_db);
        _ledger.SecurityAudit += (sender, args) => 
        {
             File.AppendAllText("C:\\Code\\Sharc\\debug_trace.log", $"[AUDIT] {args.EventType}: {args.Details}\n");
        };

        // Seed some data
        using var tx = _db.BeginTransaction();
        for (int i = 0; i < 100; i++)
        {
            _ledger.Append($"Payload {i}", _signer, tx);
        }
        tx.Commit();
    }

    public void Dispose()
    {
        _db?.Dispose();
        _signer?.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VerifyIntegrity_ShouldPass()
    {
        Assert.True(_ledger.VerifyIntegrity());
    }

    [Fact]
    public void AppendSingle_ShouldSucceed()
    {
        using var tx = _db.BeginTransaction();
        _ledger.Append("New Payload", _signer, tx);
        tx.Commit();
        Assert.True(_ledger.VerifyIntegrity());
    }
}
