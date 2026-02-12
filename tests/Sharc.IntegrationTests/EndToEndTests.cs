/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc;
using Sharc.IntegrationTests.Helpers;
using Sharc.Core.Schema;
using Xunit;

namespace Sharc.IntegrationTests;

public class EndToEndTests
{
    // --- Empty Database ---

    [Fact]
    public void OpenMemory_EmptyDatabase_HasNoUserTables()
    {
        var data = TestDatabaseFactory.CreateEmptyDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.NotNull(db.Schema);
        Assert.Empty(db.Schema.Tables);
    }

    // --- Simple Table: users ---

    [Fact]
    public void OpenMemory_UsersTable_SchemaHasOneTable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Single(db.Schema.Tables);
        Assert.Equal("users", db.Schema.Tables[0].Name);
    }

    [Fact]
    public void OpenMemory_UsersTable_ColumnsMatchSchema()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var table = db.Schema.Tables[0];
        var colNames = table.Columns.Select(c => c.Name).ToList();

        Assert.Equal(5, table.Columns.Count);
        Assert.Contains("id", colNames);
        Assert.Contains("name", colNames);
        Assert.Contains("age", colNames);
        Assert.Contains("balance", colNames);
        Assert.Contains("avatar", colNames);
    }

    [Fact]
    public void OpenMemory_UsersTable_ReadAllRows_CorrectCount()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void OpenMemory_UsersTable_ReadAllRows_CorrectValues()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(3);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");

        Assert.True(reader.Read());
        Assert.Equal("User1", reader.GetString(1));
        Assert.Equal(21, reader.GetInt32(2));
        Assert.Equal(101.50, reader.GetDouble(3), 2);
        var blob = reader.GetBlob(4);
        Assert.Equal(3, blob.Length);
        Assert.Equal(1, blob[0]);

        Assert.True(reader.Read());
        Assert.Equal("User2", reader.GetString(1));
        Assert.Equal(22, reader.GetInt32(2));

        Assert.True(reader.Read());
        Assert.Equal("User3", reader.GetString(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void GetRowCount_UsersTable_ReturnsCorrectCount()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(15);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Equal(15, db.GetRowCount("users"));
    }

    // --- All Types ---

    [Fact]
    public void OpenMemory_AllTypes_IntegerValues_Correct()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");

        // Row 1: int_val = 42
        Assert.True(reader.Read());
        Assert.Equal(42L, reader.GetInt64(1));

        // Row 2: int_val = 0
        Assert.True(reader.Read());
        Assert.Equal(0L, reader.GetInt64(1));

        // Row 3: int_val = -999
        Assert.True(reader.Read());
        Assert.Equal(-999L, reader.GetInt64(1));

        // Row 4: int_val = long.MaxValue
        Assert.True(reader.Read());
        Assert.Equal(long.MaxValue, reader.GetInt64(1));
    }

    [Fact]
    public void OpenMemory_AllTypes_FloatValues_Correct()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");

        // Row 1: real_val = 3.14159
        Assert.True(reader.Read());
        Assert.Equal(3.14159, reader.GetDouble(2), 5);

        // Row 2: real_val = 0.0
        Assert.True(reader.Read());
        Assert.Equal(0.0, reader.GetDouble(2), 5);

        // Row 3: real_val = -1.5
        Assert.True(reader.Read());
        Assert.Equal(-1.5, reader.GetDouble(2), 5);
    }

    [Fact]
    public void OpenMemory_AllTypes_TextValues_Correct()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");

        // Row 1: text_val = "Hello, Sharc!"
        Assert.True(reader.Read());
        Assert.Equal("Hello, Sharc!", reader.GetString(3));

        // Row 2: text_val = "" (empty string)
        Assert.True(reader.Read());
        Assert.Equal("", reader.GetString(3));

        // Row 3: text_val = "negative"
        Assert.True(reader.Read());
        Assert.Equal("negative", reader.GetString(3));

        // Row 4: text_val = 500 X's
        Assert.True(reader.Read());
        Assert.Equal(new string('X', 500), reader.GetString(3));
    }

    [Fact]
    public void OpenMemory_AllTypes_BlobValues_Correct()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");

        // Row 1: blob_val = [0xDE, 0xAD, 0xBE, 0xEF]
        Assert.True(reader.Read());
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, reader.GetBlob(4));

        // Row 2: blob_val = empty
        Assert.True(reader.Read());
        Assert.Equal(Array.Empty<byte>(), reader.GetBlob(4));

        // Row 3: blob_val = NULL
        Assert.True(reader.Read());
        Assert.True(reader.IsNull(4));
    }

    [Fact]
    public void OpenMemory_AllTypes_NullColumn_IsNull()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");

        // All rows have null_val = NULL
        while (reader.Read())
        {
            Assert.True(reader.IsNull(5));
        }
    }

    // --- Multi-Table ---

    [Fact]
    public void OpenMemory_MultiTable_AllTablesListed()
    {
        var data = TestDatabaseFactory.CreateMultiTableDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var tableNames = db.Schema.Tables.Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Equal(5, db.Schema.Tables.Count);
        Assert.Contains("products", tableNames);
        Assert.Contains("orders", tableNames);
        Assert.Contains("customers", tableNames);
        Assert.Contains("categories", tableNames);
        Assert.Contains("reviews", tableNames);
    }

    [Fact]
    public void OpenMemory_MultiTable_EachTableReadable()
    {
        var data = TestDatabaseFactory.CreateMultiTableDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        foreach (var table in db.Schema.Tables)
        {
            using var reader = db.CreateReader(table.Name);
            int count = 0;
            while (reader.Read())
                count++;
            Assert.True(count > 0, $"Table '{table.Name}' should have at least one row");
        }
    }

    // --- Indexed Table ---

    [Fact]
    public void OpenMemory_IndexedTable_IndexInfoInSchema()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.True(db.Schema.Indexes.Count >= 2, "Should have at least 2 indexes");

        var indexNames = db.Schema.Indexes.Select(i => i.Name).ToList();
        Assert.Contains("idx_items_name", indexNames);
        Assert.Contains("idx_items_category", indexNames);
    }

    [Fact]
    public void OpenMemory_IndexedTable_DataStillReadable()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Equal(20, db.GetRowCount("items"));
    }

    [Fact]
    public void OpenMemory_IndexedTable_IndexColumnsPopulated()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var nameIndex = db.Schema.Indexes.First(i => i.Name == "idx_items_name");
        Assert.Single(nameIndex.Columns);
        Assert.Equal("name", nameIndex.Columns[0].Name);
        Assert.Equal(0, nameIndex.Columns[0].Ordinal);
        Assert.False(nameIndex.Columns[0].IsDescending);

        var catIndex = db.Schema.Indexes.First(i => i.Name == "idx_items_category");
        Assert.Single(catIndex.Columns);
        Assert.Equal("category", catIndex.Columns[0].Name);
    }

    // --- Large Table ---

    [Fact]
    public void OpenMemory_LargeTable_ReadAllRows()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(1000);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("large_table");
        int count = 0;
        long lastNumber = 0;
        while (reader.Read())
        {
            count++;
            var number = reader.GetInt64(2);
            Assert.Equal(count * 100L, number);
            lastNumber = number;
        }

        Assert.Equal(1000, count);
        Assert.Equal(100_000L, lastNumber);
    }

    // --- Views ---

    [Fact]
    public void OpenMemory_DatabaseWithViews_ViewInSchema()
    {
        var data = TestDatabaseFactory.CreateDatabaseWithViews();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Single(db.Schema.Views);
        Assert.Equal("eng_employees", db.Schema.Views[0].Name);
    }

    [Fact]
    public void OpenMemory_DatabaseWithViews_TableStillReadable()
    {
        var data = TestDatabaseFactory.CreateDatabaseWithViews();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Equal(3, db.GetRowCount("employees"));

        using var reader = db.CreateReader("employees");
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(1));
    }

    // --- Info ---

    [Fact]
    public void OpenMemory_DatabaseInfo_CorrectPageSize()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Equal(4096, db.Info.PageSize);
    }

    // --- Column Projection ---

    [Fact]
    public void CreateReader_WithColumnProjection_ReturnsOnlyRequestedColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(3);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users", "name", "age");

        Assert.Equal(2, reader.FieldCount);

        Assert.True(reader.Read());
        Assert.Equal("User1", reader.GetString(0));
        Assert.Equal(21, reader.GetInt32(1));
    }

    [Fact]
    public void CreateReader_ColumnNames_MatchProjection()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users", "balance", "name");

        Assert.Equal("balance", reader.GetColumnName(0));
        Assert.Equal("name", reader.GetColumnName(1));
    }

    // --- Error Cases ---

    [Fact]
    public void CreateReader_NonexistentTable_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<KeyNotFoundException>(() => db.CreateReader("nonexistent"));
    }

    // --- File-backed ---

    [Fact]
    public void Open_FileBackedDatabase_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        var tempPath = Path.Combine(Path.GetTempPath(), $"sharc_e2e_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(tempPath, data);

            using var db = SharcDatabase.Open(tempPath);
            Assert.Equal(5, db.GetRowCount("users"));

            using var reader = db.CreateReader("users");
            Assert.True(reader.Read());
            Assert.Equal("User1", reader.GetString(1));
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort */ }
        }
    }

    // --- GetValue (boxed) ---

    [Fact]
    public void GetValue_ReturnsCorrectBoxedTypes()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");
        Assert.True(reader.Read());

        // id column (INTEGER PRIMARY KEY) = 1
        var id = reader.GetValue(0);
        Assert.IsType<long>(id);

        // int_val = 42
        var intVal = reader.GetValue(1);
        Assert.IsType<long>(intVal);
        Assert.Equal(42L, intVal);

        // real_val = 3.14159
        var realVal = reader.GetValue(2);
        Assert.IsType<double>(realVal);

        // text_val = "Hello, Sharc!"
        var textVal = reader.GetValue(3);
        Assert.IsType<string>(textVal);
        Assert.Equal("Hello, Sharc!", textVal);

        // null_val = NULL
        var nullVal = reader.GetValue(5);
        Assert.Equal(DBNull.Value, nullVal);
    }

    // --- Seek (B-Tree Point Lookup) ---

    [Fact]
    public void Seek_ExistingRowId_ReturnsTrue_CorrectValues()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        Assert.True(reader.Seek(5));
        Assert.Equal(5, reader.RowId);
        Assert.Equal("User5", reader.GetString(1));
        Assert.Equal(25, reader.GetInt32(2));
    }

    [Fact]
    public void Seek_NonExistentRowId_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        Assert.False(reader.Seek(999));
    }

    [Fact]
    public void Seek_FirstRow_ReturnsFirstRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        Assert.True(reader.Seek(1));
        Assert.Equal("User1", reader.GetString(1));
    }

    [Fact]
    public void Seek_LastRow_ReturnsLastRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        Assert.True(reader.Seek(10));
        Assert.Equal("User10", reader.GetString(1));
    }

    [Fact]
    public void Seek_ThenRead_ContinuesFromSeekPosition()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users");
        Assert.True(reader.Seek(5));
        Assert.Equal("User5", reader.GetString(1));

        // Read() should advance to the next row after the seek position
        Assert.True(reader.Read());
        Assert.Equal("User6", reader.GetString(1));
    }

    [Fact]
    public void Seek_WithProjection_DecodesOnlyRequestedColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("users", "name", "age");
        Assert.True(reader.Seek(7));
        Assert.Equal("User7", reader.GetString(0));
        Assert.Equal(27, reader.GetInt32(1));
    }

    [Fact]
    public void Seek_LargeTable_FindsMiddleRow()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(1000);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("large_table");
        Assert.True(reader.Seek(500));
        Assert.Equal(500, reader.RowId);
    }

    // --- GetColumnType ---

    [Fact]
    public void GetColumnType_ReturnsCorrectTypes()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("all_types");
        Assert.True(reader.Read());

        Assert.Equal(SharcColumnType.Integral, reader.GetColumnType(0)); // id
        Assert.Equal(SharcColumnType.Integral, reader.GetColumnType(1)); // int_val
        Assert.Equal(SharcColumnType.Real, reader.GetColumnType(2));   // real_val
        Assert.Equal(SharcColumnType.Text, reader.GetColumnType(3));    // text_val
        Assert.Equal(SharcColumnType.Blob, reader.GetColumnType(4));    // blob_val
        Assert.Equal(SharcColumnType.Null, reader.GetColumnType(5));    // null_val
    }
}
