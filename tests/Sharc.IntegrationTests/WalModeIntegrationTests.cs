using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for WAL mode database reading.
/// Creates real WAL-mode databases with Microsoft.Data.Sqlite
/// and verifies Sharc can read data from both the main file and WAL.
/// </summary>
public class WalModeIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public WalModeIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharc_wal_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Open_WalModeDatabase_DoesNotThrow()
    {
        var dbPath = CreateWalDatabase("wal_basic", conn =>
        {
            Execute(conn, "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT)");
            Execute(conn, "INSERT INTO items VALUES (1, 'Hello')");
        });

        using var db = SharcDatabase.Open(dbPath);

        Assert.NotNull(db);
        Assert.NotNull(db.Schema);
    }

    [Fact]
    public void Open_WalModeDatabase_CanReadTable()
    {
        var dbPath = CreateWalDatabase("wal_read", conn =>
        {
            Execute(conn, "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL)");
            Execute(conn, "INSERT INTO products VALUES (1, 'Widget', 9.99)");
            Execute(conn, "INSERT INTO products VALUES (2, 'Gadget', 19.99)");
        });

        using var db = SharcDatabase.Open(dbPath);
        using var reader = db.CreateReader("products");

        var rows = new List<(long id, string name, double price)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Widget", rows[0].name);
        Assert.Equal("Gadget", rows[1].name);
    }

    [Fact]
    public void Open_WalModeDatabase_ReadsUncheckpointedData()
    {
        // Use two concurrent connections to prevent WAL checkpoint-on-close.
        // The first connection holds the WAL open while we read with Sharc.
        var dbPath = Path.Combine(_tempDir, "wal_uncheckpointed.db");
        var connStr = $"Data Source={dbPath}";

        // Connection 1: create table + initial data, holds WAL open
        using var holdConn = new SqliteConnection(connStr);
        holdConn.Open();
        Execute(holdConn, "PRAGMA journal_mode=WAL");
        Execute(holdConn, "PRAGMA wal_autocheckpoint=0");
        Execute(holdConn, "CREATE TABLE events (id INTEGER PRIMARY KEY, name TEXT)");
        Execute(holdConn, "INSERT INTO events VALUES (1, 'First')");

        // Connection 2: write more data (goes to WAL, not checkpointed)
        using (var writeConn = new SqliteConnection(connStr))
        {
            writeConn.Open();
            Execute(writeConn, "INSERT INTO events VALUES (2, 'Second')");
            Execute(writeConn, "INSERT INTO events VALUES (3, 'Third')");
        }

        // WAL file should exist because holdConn is still open
        var walPath = dbPath + "-wal";
        Assert.True(File.Exists(walPath), "WAL file should exist while a connection is held open");

        // Read with Sharc while WAL data exists
        using var db = SharcDatabase.Open(dbPath, new SharcOpenOptions { FileShareMode = FileShare.ReadWrite });
        using var reader = db.CreateReader("events");

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        Assert.Contains("First", names);
        Assert.Contains("Second", names);
        Assert.Contains("Third", names);
    }

    [Fact]
    public void Open_WalModeDatabase_SchemaReadCorrectly()
    {
        var dbPath = CreateWalDatabase("wal_schema", conn =>
        {
            Execute(conn, "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER)");
            Execute(conn, "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER)");
        });

        using var db = SharcDatabase.Open(dbPath);

        Assert.True(db.Schema.Tables.Count >= 2);
        Assert.NotNull(db.Schema.Tables.FirstOrDefault(t => t.Name == "users"));
        Assert.NotNull(db.Schema.Tables.FirstOrDefault(t => t.Name == "orders"));
    }

    [Fact]
    public void Open_WalModeDatabase_InfoShowsWalMode()
    {
        var dbPath = CreateWalDatabase("wal_info", conn =>
        {
            Execute(conn, "CREATE TABLE x (id INTEGER PRIMARY KEY)");
        });

        using var db = SharcDatabase.Open(dbPath);

        Assert.True(db.Info.IsWalMode);
    }

    #region Helpers

    private string CreateWalDatabase(string name, Action<SqliteConnection> setup)
    {
        var dbPath = Path.Combine(_tempDir, $"{name}.db");
        var connStr = $"Data Source={dbPath}";
        using (var conn = new SqliteConnection(connStr))
        {
            conn.Open();
            Execute(conn, "PRAGMA journal_mode=WAL");
            setup(conn);
        }
        SqliteConnection.ClearPool(new SqliteConnection(connStr));
        return dbPath;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    #endregion
}
