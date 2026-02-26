/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Microsoft.Data.Sqlite;

namespace Sharc.IntegrationTests.Helpers;

/// <summary>
/// Creates real SQLite databases in-memory using Microsoft.Data.Sqlite,
/// then exports them as byte arrays for Sharc to read.
/// </summary>
internal static class TestDatabaseFactory
{
    /// <summary>
    /// Creates an empty database (only sqlite_schema, no user tables).
    /// </summary>
    public static byte[] CreateEmptyDatabase()
    {
        return CreateDatabase(conn =>
        {
            // Force SQLite to write the schema page by creating and dropping a table
            Execute(conn, "CREATE TABLE _init_ (x INTEGER)");
            Execute(conn, "DROP TABLE _init_");
        });
    }

    /// <summary>
    /// Creates a simple users table with mixed types.
    /// </summary>
    public static byte[] CreateUsersDatabase(int rowCount = 10)
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, """
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    age INTEGER,
                    balance REAL,
                    avatar BLOB
                )
            """);

            for (int i = 1; i <= rowCount; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO users (name, age, balance, avatar) VALUES ($name, $age, $balance, $avatar)";
                cmd.Parameters.AddWithValue("$name", $"User{i}");
                cmd.Parameters.AddWithValue("$age", 20 + i);
                cmd.Parameters.AddWithValue("$balance", 100.50 + i);
                cmd.Parameters.AddWithValue("$avatar", new byte[] { (byte)i, 0xFF, 0xAB });
                cmd.ExecuteNonQuery();
            }
        });
    }

    /// <summary>
    /// Creates a table exercising all SQLite storage classes including NULLs.
    /// </summary>
    public static byte[] CreateAllTypesDatabase()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, """
                CREATE TABLE all_types (
                    id INTEGER PRIMARY KEY,
                    int_val INTEGER,
                    real_val REAL,
                    text_val TEXT,
                    blob_val BLOB,
                    null_val TEXT
                )
            """);

            // Row 1: all types populated
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO all_types (int_val, real_val, text_val, blob_val, null_val) VALUES ($i, $r, $t, $b, NULL)";
                cmd.Parameters.AddWithValue("$i", 42L);
                cmd.Parameters.AddWithValue("$r", 3.14159);
                cmd.Parameters.AddWithValue("$t", "Hello, Sharc!");
                cmd.Parameters.AddWithValue("$b", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
                cmd.ExecuteNonQuery();
            }

            // Row 2: zero/empty values
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO all_types (int_val, real_val, text_val, blob_val, null_val) VALUES (0, 0.0, '', $b, NULL)";
                cmd.Parameters.AddWithValue("$b", Array.Empty<byte>());
                cmd.ExecuteNonQuery();
            }

            // Row 3: negative values
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO all_types (int_val, real_val, text_val, blob_val, null_val) VALUES (-999, -1.5, 'negative', NULL, NULL)";
                cmd.ExecuteNonQuery();
            }

            // Row 4: large values
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO all_types (int_val, real_val, text_val, blob_val, null_val) VALUES ($i, $r, $t, $b, NULL)";
                cmd.Parameters.AddWithValue("$i", long.MaxValue);
                cmd.Parameters.AddWithValue("$r", double.MaxValue);
                cmd.Parameters.AddWithValue("$t", new string('X', 500));
                cmd.Parameters.AddWithValue("$b", new byte[256]);
                cmd.ExecuteNonQuery();
            }

            // Row 5: constant 1
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO all_types (int_val, real_val, text_val, blob_val, null_val) VALUES (1, 1.0, '1', $b, NULL)";
                cmd.Parameters.AddWithValue("$b", new byte[] { 1 });
                cmd.ExecuteNonQuery();
            }
        });
    }

    /// <summary>
    /// Creates multiple tables with varied schemas.
    /// </summary>
    public static byte[] CreateMultiTableDatabase()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL)");
            Execute(conn, "CREATE TABLE orders (id INTEGER PRIMARY KEY, product_id INTEGER, quantity INTEGER)");
            Execute(conn, "CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT)");
            Execute(conn, "CREATE TABLE categories (id INTEGER PRIMARY KEY, label TEXT)");
            Execute(conn, "CREATE TABLE reviews (id INTEGER PRIMARY KEY, product_id INTEGER, rating INTEGER, comment TEXT)");

            Execute(conn, "INSERT INTO products VALUES (1, 'Widget', 9.99)");
            Execute(conn, "INSERT INTO products VALUES (2, 'Gadget', 19.99)");
            Execute(conn, "INSERT INTO orders VALUES (1, 1, 5)");
            Execute(conn, "INSERT INTO orders VALUES (2, 2, 3)");
            Execute(conn, "INSERT INTO customers VALUES (1, 'Alice', 'alice@test.com')");
            Execute(conn, "INSERT INTO categories VALUES (1, 'Electronics')");
            Execute(conn, "INSERT INTO reviews VALUES (1, 1, 5, 'Great!')");
        });
    }

    /// <summary>
    /// Creates a table with an index.
    /// </summary>
    public static byte[] CreateIndexedDatabase()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, category TEXT)");
            Execute(conn, "CREATE INDEX idx_items_name ON items (name)");
            Execute(conn, "CREATE INDEX idx_items_category ON items (category)");

            for (int i = 1; i <= 20; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO items (name, category) VALUES ($name, $cat)";
                cmd.Parameters.AddWithValue("$name", $"Item{i}");
                cmd.Parameters.AddWithValue("$cat", i % 2 == 0 ? "even" : "odd");
                cmd.ExecuteNonQuery();
            }
        });
    }

    /// <summary>
    /// Creates a table with an integer-indexed column.
    /// </summary>
    public static byte[] CreateIndexedIntegerDatabase()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE events (id INTEGER PRIMARY KEY, user_id INTEGER, event_type TEXT)");
            Execute(conn, "CREATE INDEX idx_events_user_id ON events (user_id)");

            for (int i = 1; i <= 50; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO events (user_id, event_type) VALUES ($uid, $type)";
                cmd.Parameters.AddWithValue("$uid", (i % 5) + 1); // user_id 1-5
                cmd.Parameters.AddWithValue("$type", i % 2 == 0 ? "click" : "view");
                cmd.ExecuteNonQuery();
            }
        });
    }

    /// <summary>
    /// Creates a table with an indexed REAL column.
    /// </summary>
    public static byte[] CreateIndexedRealDatabase()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE points (id INTEGER PRIMARY KEY, x REAL, label TEXT)");
            Execute(conn, "CREATE INDEX idx_points_x ON points (x)");

            for (int i = 0; i < 20; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO points (x, label) VALUES ($x, $label)";
                cmd.Parameters.AddWithValue("$x", i * 0.5); // 0.0, 0.5, ..., 9.5
                cmd.Parameters.AddWithValue("$label", $"P{i}");
                cmd.ExecuteNonQuery();
            }
        });
    }

    /// <summary>
    /// Creates a 2D points table with REAL x/y coordinates and indexes suitable for
    /// composite-range and rowid-intersection planner tests.
    /// </summary>
    public static byte[] CreateIndexedReal2dDatabase(bool withCompositeIndex)
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE points2d (id INTEGER PRIMARY KEY, x REAL, y REAL, label TEXT)");
            Execute(conn, "CREATE INDEX idx_points2d_x ON points2d (x)");
            Execute(conn, "CREATE INDEX idx_points2d_y ON points2d (y)");
            if (withCompositeIndex)
                Execute(conn, "CREATE INDEX idx_points2d_xy ON points2d (x, y)");

            for (int x = 0; x <= 9; x++)
            {
                for (int y = 0; y <= 9; y++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO points2d (x, y, label) VALUES ($x, $y, $label)";
                    cmd.Parameters.AddWithValue("$x", (double)x);
                    cmd.Parameters.AddWithValue("$y", y * 0.5);
                    cmd.Parameters.AddWithValue("$label", $"P{x}_{y}");
                    cmd.ExecuteNonQuery();
                }
            }
        });
    }

    /// <summary>
    /// Creates a table with a large number of rows to test multi-page b-trees.
    /// </summary>
    public static byte[] CreateLargeDatabase(int rowCount = 1000)
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE large_table (id INTEGER PRIMARY KEY, value TEXT, number INTEGER)");

            using var transaction = conn.BeginTransaction();
            for (int i = 1; i <= rowCount; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO large_table (value, number) VALUES ($v, $n)";
                cmd.Parameters.AddWithValue("$v", $"Row number {i} with some padding text to make the record bigger");
                cmd.Parameters.AddWithValue("$n", i * 100L);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        });
    }

    /// <summary>
    /// Creates a table with views.
    /// </summary>
    public static byte[] CreateDatabaseWithViews()
    {
        return CreateDatabase(conn =>
        {
            Execute(conn, "CREATE TABLE employees (id INTEGER PRIMARY KEY, name TEXT, dept TEXT, salary REAL)");
            Execute(conn, "INSERT INTO employees VALUES (1, 'Alice', 'Eng', 120000.0)");
            Execute(conn, "INSERT INTO employees VALUES (2, 'Bob', 'Eng', 110000.0)");
            Execute(conn, "INSERT INTO employees VALUES (3, 'Carol', 'Sales', 95000.0)");

            Execute(conn, "CREATE VIEW eng_employees AS SELECT * FROM employees WHERE dept = 'Eng'");
        });
    }

    /// <summary>
    /// Creates a custom database with the given setup action.
    /// </summary>
    public static byte[] CreateDatabaseWith(Action<SqliteConnection> setup)
    {
        return CreateDatabase(setup);
    }

    /// <summary>
    /// Creates a database and exports it as a byte array.
    /// Uses a shared-cache in-memory database, then backs it up to a file-backed temp db
    /// to get the raw bytes.
    /// </summary>
    private static byte[] CreateDatabase(Action<SqliteConnection> setup)
    {
        // Use a temp file to get the raw bytes
        var tempPath = Path.Combine(Path.GetTempPath(), $"sharc_test_{Guid.NewGuid():N}.db");
        try
        {
            var connString = $"Data Source={tempPath}";
            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();
                setup(conn);
            }
            // Clear connection pool to release the file handle on Windows
            SqliteConnection.ClearPool(new SqliteConnection(connString));
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    internal static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
