using System.IO;
using System.Buffers;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Xunit;

namespace Sharc.Tests.Write;

internal static class SharcDatabaseExtensions
{
    public static void Execute(this SharcDatabase db, string sql, AgentInfo? agent = null)
    {
        using var tx = db.BeginTransaction();
        tx.Execute(sql, agent);
        tx.Commit();
    }
}

public sealed class SharcSchemaWriterTests
{
    private const int PageSize = 4096;

    private static SharcDatabase CreateTestDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
        return SharcDatabase.Create(path);
    }

    [Fact]
    public void CreateTable_IfNotExists_DoesNotThrowIfTableExists()
    {
        using var db = CreateTestDatabase();
        Assert.Equal(5, db.Schema.Tables.Count); // sqlite_master + 4 sharc system tables
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        Assert.Equal(6, db.Schema.Tables.Count);
        
        // Should not throw
        db.Execute("CREATE TABLE IF NOT EXISTS T1 (id INTEGER)");
    }

    [Fact]
    public void CreateTable_ThrowsIfTableExistsWithoutIfNotExists()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        
        Assert.Throws<InvalidOperationException>(() => db.Execute("CREATE TABLE T1 (id INTEGER)"));
    }

    [Fact]
    public void AlterTable_AddColumn_UpdatesSqliteMaster()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        db.Execute("ALTER TABLE T1 ADD COLUMN name TEXT");

        var table = db.Schema.GetTable("T1");
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("name", table.Columns[1].Name);
        Assert.Equal("TEXT", table.Columns[1].DeclaredType);

        // Verify the SQL in sqlite_master is updated
        using var reader = db.CreateReader("sqlite_master");
        bool found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "T1")
            {
                string sql = reader.GetString(4);
                Assert.Contains("name TEXT", sql);
                found = true;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void AlterTable_RenameTo_UpdatesSqliteMaster()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE OldName (id INTEGER)");
        db.Execute("ALTER TABLE OldName RENAME TO NewName");

        Assert.Throws<KeyNotFoundException>(() => db.Schema.GetTable("OldName"));
        var table = db.Schema.GetTable("NewName");
        Assert.NotNull(table);

        // Verify sqlite_master
        using var reader = db.CreateReader("sqlite_master");
        bool found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "NewName")
            {
                Assert.Equal("table", reader.GetString(0));
                Assert.Equal("NewName", reader.GetString(2)); // tbl_name
                Assert.Contains("CREATE TABLE NewName", reader.GetString(4));
                found = true;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void CreateTable_QuotedIdentifiers_HandledCorrectly()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE \"Complex Table Name\" ([Wrapped Col] INTEGER)");

        var table = db.Schema.GetTable("Complex Table Name");
        Assert.NotNull(table);
        Assert.Equal("Wrapped Col", table.Columns[0].Name);
    }

    [Fact]
    public void CreateTable_AgentEnforcement_ThrowsIfUnauthorized()
    {
        using var db = CreateTestDatabase();
        var agent = new AgentInfo(
            "UnauthorizedAgent", 
            AgentClass.User, 
            new byte[32], 
            0, // AuthorityCeiling
            "", // WriteScope
            "", // ReadScope
            0, 0, // Validity
            "", // Parent
            false, // CoSign
            new byte[64] // Signature
        );

        Assert.Throws<UnauthorizedAccessException>(() => db.Execute("CREATE TABLE Forbidden (id INTEGER)", agent));
    }
}
