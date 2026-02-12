/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc;
using Sharc.Exceptions;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public class EdgeCaseTests
{
    // --- Empty table ---

    [Fact]
    public void CreateReader_EmptyTable_ReadReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE empty_table (id INTEGER PRIMARY KEY, name TEXT)");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("empty_table");

        Assert.False(reader.Read());
    }

    // --- Single row ---

    [Fact]
    public void CreateReader_SingleRow_ReadsAndStops()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE single (id INTEGER PRIMARY KEY, val TEXT)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO single (val) VALUES ('only')");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("single");

        Assert.True(reader.Read());
        Assert.Equal("only", reader.GetString(1));
        Assert.False(reader.Read());
    }

    // --- Read after end ---

    [Fact]
    public void Read_CalledAfterEnd_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE tiny (id INTEGER PRIMARY KEY)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO tiny DEFAULT VALUES");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("tiny");

        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.False(reader.Read()); // second call after end still returns false
    }

    // --- Dispose idempotency ---

    [Fact]
    public void SharcDatabase_DisposeCalledTwice_NoThrow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        var db = SharcDatabase.OpenMemory(data);
        db.Dispose();
        db.Dispose(); // should not throw
    }

    [Fact]
    public void SharcDataReader_DisposeCalledTwice_NoThrow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        var reader = db.CreateReader("users");
        reader.Dispose();
        reader.Dispose(); // should not throw
    }

    // --- Access after dispose ---

    [Fact]
    public void SharcDatabase_AccessAfterDispose_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        var db = SharcDatabase.OpenMemory(data);
        db.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.CreateReader("users"));
        Assert.Throws<ObjectDisposedException>(() => _ = db.Schema);
        Assert.Throws<ObjectDisposedException>(() => _ = db.Info);
    }

    // --- WITHOUT ROWID ---

    [Fact]
    public void CreateReader_WithoutRowIdTable_ThrowsUnsupportedFeature()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE wr (key TEXT PRIMARY KEY, val TEXT) WITHOUT ROWID");
            TestDatabaseFactory.Execute(conn, "INSERT INTO wr VALUES ('a', 'b')");
        });

        using var db = SharcDatabase.OpenMemory(data);

        var ex = Assert.Throws<UnsupportedFeatureException>(() => db.CreateReader("wr"));
        Assert.Contains("WITHOUT ROWID", ex.Message);
    }

    // --- PreloadToMemory option ---

    [Fact]
    public void Open_PreloadToMemory_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(3);
        var tempPath = Path.Combine(Path.GetTempPath(), $"sharc_preload_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(tempPath, data);

            using var db = SharcDatabase.Open(tempPath, new SharcOpenOptions { PreloadToMemory = true });
            Assert.Equal(3, db.GetRowCount("users"));
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort */ }
        }
    }

    // --- RowId exposure ---

    [Fact]
    public void RowId_ReturnsSequentialIds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users");

        var rowIds = new List<long>();
        while (reader.Read())
            rowIds.Add(reader.RowId);

        Assert.Equal(5, rowIds.Count);
        // SQLite auto-increment starts at 1
        Assert.Equal(1L, rowIds[0]);
        Assert.Equal(5L, rowIds[4]);
    }

    // --- Unicode text ---

    [Fact]
    public void ReadString_UnicodeContent_DecodesCorrectly()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE unicode (id INTEGER PRIMARY KEY, text_val TEXT)");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO unicode (text_val) VALUES ($t)";
            cmd.Parameters.AddWithValue("$t", "Hello \u00e9\u00e8\u00ea \u4e16\u754c \ud83c\udf0d");
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("unicode");

        Assert.True(reader.Read());
        Assert.Equal("Hello \u00e9\u00e8\u00ea \u4e16\u754c \ud83c\udf0d", reader.GetString(1));
    }

    // --- Large BLOB ---

    [Fact]
    public void ReadBlob_LargeBlob_ReadsCorrectly()
    {
        var bigBlob = new byte[8192];
        for (int i = 0; i < bigBlob.Length; i++)
            bigBlob[i] = (byte)(i % 256);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE blobs (id INTEGER PRIMARY KEY, data BLOB)");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO blobs (data) VALUES ($d)";
            cmd.Parameters.AddWithValue("$d", bigBlob);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("blobs");

        Assert.True(reader.Read());
        var result = reader.GetBlob(1);
        Assert.Equal(bigBlob.Length, result.Length);
        Assert.Equal(bigBlob, result);
    }

    // --- FieldCount ---

    [Fact]
    public void FieldCount_NoProjection_ReturnsAllColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users");

        Assert.Equal(5, reader.FieldCount);
    }

    [Fact]
    public void FieldCount_WithProjection_ReturnsProjectedCount()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users", "name");

        Assert.Equal(1, reader.FieldCount);
    }

    // --- Column name retrieval ---

    [Fact]
    public void GetColumnName_ReturnsCorrectNames()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users");

        Assert.Equal("id", reader.GetColumnName(0));
        Assert.Equal("name", reader.GetColumnName(1));
        Assert.Equal("age", reader.GetColumnName(2));
        Assert.Equal("balance", reader.GetColumnName(3));
        Assert.Equal("avatar", reader.GetColumnName(4));
    }

    // --- Invalid column in projection ---

    [Fact]
    public void CreateReader_InvalidColumnName_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<ArgumentException>(() => db.CreateReader("users", "nonexistent"));
    }

    // --- GetValue before Read ---

    [Fact]
    public void GetValue_BeforeRead_ThrowsInvalidOperation()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users");

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    // --- Page cache option ---

    [Fact]
    public void OpenMemory_WithPageCache_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { PageCacheSize = 100 });
        Assert.Equal(5, db.GetRowCount("users"));
    }
}
