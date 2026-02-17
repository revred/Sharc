using Xunit;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc; // Added for SharcWriter
using Sharc.IntegrationTests.Helpers; // TestDatabaseFactory

namespace Sharc.IntegrationTests;

public class SimpleWriteTests : IDisposable
{
    private readonly string _dbPath;
    
    public SimpleWriteTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"simple_write_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void CanInsertAndQuery_SimpleTable()
    {
        // 1. Create DB with Schema (using SQLite)
        var data = Sharc.IntegrationTests.Helpers.TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        // 2. Insert with SharcWriter
        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            
            // Insert 1
            writer.Insert("users", 
                ColumnValue.FromInt64(1, 1), 
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
                ColumnValue.FromInt64(2, 30), // Age = 30 (serial type 2 for short)
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null()
            );
            
            // Insert 2
            writer.Insert("users", 
                ColumnValue.FromInt64(1, 2), 
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Bob")),
                ColumnValue.FromInt64(2, 25), // Age = 25
                ColumnValue.FromDouble(50.0),
                ColumnValue.Null()
            );
        }

        // 3. Verify Read with Sharc
        using (var db = SharcDatabase.Open(_dbPath))
        {
            var reader = db.CreateReader("users", "name");
            
            Assert.True(reader.Read());
            Assert.Equal("Alice", reader.GetString(0));
            
            Assert.True(reader.Read());
            Assert.Equal("Bob", reader.GetString(0));
            
            Assert.False(reader.Read());
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
