using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class AuditTests : IDisposable
{
    private const string DbPath = "audit_test.sharc";
    private SharcDatabase? _db;
    private AuditManager _audit;

    public AuditTests()
    {
        if (File.Exists(DbPath)) File.Delete(DbPath);
        _db = SharcDatabase.Create(DbPath);
        _audit = new AuditManager(_db);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(DbPath)) 
        {
            try { File.Delete(DbPath); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CanLogEvents_AndVerifyIntegrity()
    {
        // Act
        _audit.LogEvent(new SecurityEventArgs(SecurityEventType.RegistrationSuccess, "agent_001", "Initial Registration"));
        _audit.LogEvent(new SecurityEventArgs(SecurityEventType.AppendSuccess, "agent_001", "Block 1 appended"));

        // Assert
        Assert.True(_audit.VerifyIntegrity(), "Integrity check should pass for valid chain.");
    }

    [Fact]
    public void IntegrityCheck_FailsOnTampering()
    {
        // 1. Log event
        _audit.LogEvent(new SecurityEventArgs(SecurityEventType.RegistrationSuccess, "agent1", "details1"));

        // 2. Log second event
        _audit.LogEvent(new SecurityEventArgs(SecurityEventType.AppendSuccess, "agent1", "details2"));

        Assert.True(_audit.VerifyIntegrity());

        // 3. Close DB to ensure flush
        _db?.Dispose();
        _db = null;

        // 4. Corrupt the file
        // We need to be careful to corrupt the audit table content, not metadata.
        // Audit table is Page 5.
        // Page 5 starts at offset 4096 * 4 = 16384.
        // We modify a byte inside the page.
        // To be safe, modify a byte near the end of the file (likely last record).
        
        var data = File.ReadAllBytes(DbPath);
        if (data.Length > 16384 + 100) 
        {
            // Flip a bit in the last 50 bytes (likely hash or data)
            data[data.Length - 20] ^= 0xFF; 
            File.WriteAllBytes(DbPath, data);
        }

        // 5. Reopen
        using var db2 = SharcDatabase.Open(DbPath);
        var audit2 = new AuditManager(db2);
        
        Assert.False(audit2.VerifyIntegrity(), "Integrity check should fail after tampering.");
    }

    [Fact]
    public void HashChain_PersistsAcrossSessions()
    {
        _audit.LogEvent(new SecurityEventArgs(SecurityEventType.RegistrationSuccess, "agent1", "session1"));
        _db?.Dispose();
        _db = null;

        using var db2 = SharcDatabase.Open(DbPath, new SharcOpenOptions { Writable = true });
        var audit2 = new AuditManager(db2);
        
        // This log should link to the previous hash from session1
        audit2.LogEvent(new SecurityEventArgs(SecurityEventType.AppendSuccess, "agent1", "session2"));
        
        Assert.True(audit2.VerifyIntegrity());
    }
}
