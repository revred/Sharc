using Sharc.Core;
using Sharc.Core.Schema;
using Sharc;
using Sharc.Core.Query;
using Xunit; // Add Xunit namespace

namespace Sharc.IntegrationTests;

// [TestClass] - Removed as per xUnit conversion
public class DDLTests : IDisposable
{
    private string _dbPath;
    private SharcDatabase _db;

    // [TestInitialize] - Converted to constructor
    public DDLTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_ddl_{Guid.NewGuid()}.db");
        _db = SharcDatabase.Create(_dbPath);
    }

    // [TestCleanup] - Converted to IDisposable.Dispose()
    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + ".journal"))
            File.Delete(_dbPath + ".journal");
        GC.SuppressFinalize(this);
    }

    [Fact] // Converted from [TestMethod]
    public void CreateTable_Simple_Succeeds()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT, Age INTEGER)");
            tx.Commit();
        }

        // Verify schema immediately
        Assert.NotNull(_db.Schema.GetTable("Users")); // Converted from Assert.IsNotNull
        var table = _db.Schema.GetTable("Users");
        Assert.Equal(3, table.Columns.Count); // Converted from Assert.AreEqual
        Assert.Equal("Id", table.Columns[0].Name); // Converted from Assert.AreEqual
        Assert.Equal("Name", table.Columns[1].Name); // Converted from Assert.AreEqual
        Assert.Equal("Age", table.Columns[2].Name); // Converted from Assert.AreEqual

        // Verify write/read
        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users", 
                ColumnValue.FromInt64(1, 1), 
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
                ColumnValue.FromInt64(2, 30));
        }

        // Verify Schema
        table = _db.Schema.GetTable("Users");
        Assert.NotNull(table);
        Assert.Equal(3, table.Columns.Count);

        using (var reader = _db.CreateReader("Users"))
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal("Alice", reader.GetString(1));
            Assert.Equal(30, reader.GetInt64(2));
        }
    }

    [Fact] // Converted from [TestMethod]
    public void AlterTable_AddColumn_Succeeds()
    {
        // Setup initial table
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
            tx.Commit();
        }
            
        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users", ColumnValue.FromInt64(1, 1), ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")));
        }

        // Add Column
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("ALTER TABLE Users ADD COLUMN Age INTEGER");
            tx.Commit();
        }

        // Verify Schema
        var table = _db.Schema.GetTable("Users");
        Assert.Equal(3, table.Columns.Count); // Converted from Assert.AreEqual
        Assert.Equal("Age", table.Columns[2].Name); // Converted from Assert.AreEqual

        // Verify Read Old Row (Age should be NULL)
        using (var reader = _db.CreateReader("Users", filters: new[] { new SharcFilter("Id", SharcOperator.Equal, 1L) }))
        {
            Assert.True(reader.Read()); // Converted from Assert.IsTrue
            Assert.True(reader.IsNull(2)); // Converted from Assert.IsTrue
        }

        // Verify Insert New Row
        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users", 
                ColumnValue.FromInt64(1, 2), 
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Bob")),
                ColumnValue.FromInt64(2, 25));
        }

        using (var reader = _db.CreateReader("Users", filters: new[] { new SharcFilter("Id", SharcOperator.Equal, 2L) }))
        {
            Assert.True(reader.Read()); // Converted from Assert.IsTrue
            Assert.Equal(25L, reader.GetInt64(2)); // Converted from Assert.AreEqual
        }
    }

    [Fact] // Converted from [TestMethod]
    public void AlterTable_Rename_Succeeds()
    {
        // Setup
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE OldName (Id INTEGER PRIMARY KEY)");
            tx.Commit();
        }

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("OldName", ColumnValue.FromInt64(1, 1));
        }

        // Rename
        using (var tx = _db.BeginTransaction())
        {
             tx.Execute("ALTER TABLE OldName RENAME TO NewName");
             tx.Commit();
        }

        // Verify
        Assert.Throws<KeyNotFoundException>(() => _db.Schema.GetTable("OldName"));
        Assert.NotNull(_db.Schema.GetTable("NewName")); // Converted from Assert.IsNotNull

        // 4. Verify new name works
        using (var reader = _db.CreateReader("NewName"))
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt64(0));
        }
    }
}
