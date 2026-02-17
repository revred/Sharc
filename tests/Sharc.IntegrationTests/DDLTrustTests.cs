using Sharc.Core;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.IntegrationTests;

public class DDLTrustTests : IDisposable
{
    private string _dbPath;
    private SharcDatabase _db;

    public DDLTrustTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_ddl_trust_{Guid.NewGuid()}.db");
        _db = SharcDatabase.Create(_dbPath);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (File.Exists(_dbPath + ".journal")) File.Delete(_dbPath + ".journal");
        GC.SuppressFinalize(this);
    }

    private static AgentInfo CreateAgent(string writeScope)
    {
        // EntitlementEnforcer only checks validity dates, not signature integrity.
        return new AgentInfo(
            AgentId: "agent-1",
            Class: AgentClass.User,
            PublicKey: new byte[32],
            AuthorityCeiling: 1000,
            WriteScope: writeScope,
            ReadScope: "*",
            ValidityStart: 0,
            ValidityEnd: 0,
            ParentAgent: "",
            CoSignRequired: false,
            Signature: new byte[64]
        );
    }

    [Fact]
    public void CreateTable_NoAgent_Succeeds()
    {
        using var tx = _db.BeginTransaction();
        tx.Execute("CREATE TABLE SystemTable (Id INTEGER)");
        tx.Commit();
        Assert.NotNull(_db.Schema.GetTable("SystemTable"));
    }

    [Fact]
    public void CreateTable_AdminAgent_Succeeds()
    {
        var agent = CreateAgent("*");
        using var tx = _db.BeginTransaction();
        tx.Execute("CREATE TABLE AdminTable (Id INTEGER)", agent);
        tx.Commit();
        Assert.NotNull(_db.Schema.GetTable("AdminTable"));
    }

    [Fact]
    public void CreateTable_SchemaAdmin_Succeeds()
    {
        var agent = CreateAgent(".schema");
        using var tx = _db.BeginTransaction();
        tx.Execute("CREATE TABLE SchemaTable (Id INTEGER)", agent);
        tx.Commit();
        Assert.NotNull(_db.Schema.GetTable("SchemaTable"));
    }

    [Fact]
    public void CreateTable_ReadOnlyAgent_Throws()
    {
        var agent = CreateAgent("Users.read"); // No schema rights
        using var tx = _db.BeginTransaction();
        Assert.Throws<UnauthorizedAccessException>(() => 
            tx.Execute("CREATE TABLE HackedTable (Id INTEGER)", agent));
    }

    [Fact]
    public void AlterTable_TableSpecificAgent_Throws()
    {
        // Agent has write access to Users table, but NOT schema rights
        // "Users.*" means read/write rows, not alter table structure.
        var agent = CreateAgent("Users.*"); 
        
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Users (Id INTEGER)");
            tx.Commit();
        }

        using (var tx = _db.BeginTransaction())
        {
            Assert.Throws<UnauthorizedAccessException>(() => 
                tx.Execute("ALTER TABLE Users ADD COLUMN Age INTEGER", agent));
        }
    }
}
