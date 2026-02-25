// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class JoinTests : IDisposable
{
    private readonly string _dbPath;

    public JoinTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_join_test_{Guid.NewGuid()}.db");
        SetupDatabase();
    }

    private void SetupDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL);

            INSERT INTO users VALUES (1, 'Alice', 30);
            INSERT INTO users VALUES (2, 'Bob', 40);
            INSERT INTO users VALUES (3, 'Charlie', 25);

            INSERT INTO orders VALUES (10, 1, 100.5);
            INSERT INTO orders VALUES (11, 1, 200.0);
            INSERT INTO orders VALUES (12, 2, 300.0);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void InnerJoin_ReturnsMatchingRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id ORDER BY o.id");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void LeftJoin_ReturnsAllLeftRows_WithNulls()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // Order by u.id to ensure deterministic order for assertion
        using var reader = db.Query("SELECT u.name, o.amount FROM users u LEFT JOIN orders o ON u.id = o.user_id ORDER BY u.id, o.id");

        // Alice (1) -> Order 10
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        // Alice (1) -> Order 11
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        // Bob (2) -> Order 12
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        // Charlie (3) -> NULL
        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        Assert.True(reader.IsNull(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void CrossJoin_ReturnsCartesianProduct()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.id FROM users u CROSS JOIN orders o");

        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        // 3 users * 3 orders = 9 rows
        Assert.Equal(9, count);
    }

    [Fact]
    public void Join_WithWhereClause_FiltersCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id WHERE o.amount > 150 ORDER BY o.amount");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_WithTableAliases_ResolvesCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // Explicit aliases are used in the query string
        using var reader = db.Query("SELECT T1.name, T2.amount FROM users T1 JOIN orders T2 ON T1.id = T2.user_id WHERE T1.name = 'Bob'");

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    // --- RIGHT JOIN tests ---

    [Fact]
    public void RightJoin_AllMatched_EqualsInner()
    {
        // All orders have matching users, so RIGHT JOIN = INNER JOIN
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u RIGHT JOIN orders o ON u.id = o.user_id ORDER BY o.id");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void RightJoin_WithOrphanRow_EmitsNullLeft()
    {
        // Add an orphan order with user_id=99 (no matching user)
        AddOrphanOrder();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u RIGHT JOIN orders o ON u.id = o.user_id ORDER BY o.id");

        int count = 0;
        bool foundOrphan = false;
        while (reader.Read())
        {
            count++;
            var amount = reader.GetDouble(1);
            if (amount == 999.99)
            {
                Assert.True(reader.IsNull(0)); // u.name should be NULL
                foundOrphan = true;
            }
        }
        Assert.True(foundOrphan);
        Assert.Equal(4, count); // 3 matched + 1 orphan
    }

    // --- FULL OUTER JOIN tests ---

    [Fact]
    public void FullJoin_AllMatched_EqualsInner()
    {
        // All orders have matching users (Charlie has no orders but is a left orphan)
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u FULL JOIN orders o ON u.id = o.user_id ORDER BY u.id, o.id");

        // Alice -> Order 10
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        // Alice -> Order 11
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        // Bob -> Order 12
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        // Charlie -> NULL (left orphan, no orders)
        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        Assert.True(reader.IsNull(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void FullJoin_OrphansBothSides_EmitsAll()
    {
        // Charlie has no orders (left orphan), orphan order has no user (right orphan)
        AddOrphanOrder();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u FULL OUTER JOIN orders o ON u.id = o.user_id");

        int count = 0;
        bool foundLeftOrphan = false;
        bool foundRightOrphan = false;
        while (reader.Read())
        {
            count++;
            if (!reader.IsNull(0) && reader.GetString(0) == "Charlie" && reader.IsNull(1))
                foundLeftOrphan = true;
            if (reader.IsNull(0) && !reader.IsNull(1) && reader.GetDouble(1) == 999.99)
                foundRightOrphan = true;
        }
        Assert.True(foundLeftOrphan, "Expected Charlie as left orphan with null amount");
        Assert.True(foundRightOrphan, "Expected orphan order with null name");
        Assert.Equal(5, count); // 3 matched + 1 left orphan + 1 right orphan
    }

    [Fact]
    public void FullJoin_EmptyRightTable_ReturnsAllLeftWithNulls()
    {
        AddEmptyTable();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, e.user_id FROM users u FULL JOIN empty_table e ON u.id = e.user_id ORDER BY u.id");

        int count = 0;
        while (reader.Read())
        {
            Assert.True(reader.IsNull(1));
            count++;
        }
        Assert.Equal(3, count);
    }

    // --- Expanded coverage ---

    [Fact]
    public void InnerJoin_EmptyRightTable_ReturnsNoRows()
    {
        AddEmptyTable();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name FROM users u JOIN empty_table e ON u.id = e.user_id");

        Assert.False(reader.Read());
    }

    [Fact]
    public void LeftJoin_EmptyRightTable_ReturnsAllLeftWithNulls()
    {
        AddEmptyTable();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, e.user_id FROM users u LEFT JOIN empty_table e ON u.id = e.user_id ORDER BY u.id");

        int count = 0;
        while (reader.Read())
        {
            Assert.True(reader.IsNull(1));
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void InnerJoin_DuplicateKeys_ReturnsCartesian()
    {
        // Alice has 2 orders -> INNER JOIN returns 2 rows for Alice
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.id FROM users u JOIN orders o ON u.id = o.user_id WHERE u.name = 'Alice' ORDER BY o.id");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(10L, reader.GetInt64(1));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(11L, reader.GetInt64(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_WithOrderByAndLimit_ReturnsCorrectSlice()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id ORDER BY o.amount LIMIT 1");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_WithLimit_ReturnsLimitedRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id LIMIT 2");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void CrossJoin_EmptyTable_ReturnsNoRows()
    {
        AddEmptyTable();
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name FROM users u CROSS JOIN empty_table e");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_SelectSpecificColumns_ProjectsCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, u.age, o.amount FROM users u JOIN orders o ON u.id = o.user_id WHERE u.name = 'Bob'");

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(40L, reader.GetInt64(1));
        Assert.Equal(300.0, reader.GetDouble(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public void LeftJoin_WithWhereOnLeft_FiltersCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u LEFT JOIN orders o ON u.id = o.user_id WHERE u.age > 30 ORDER BY u.id");

        // Bob (age 40) has one order
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    // --- SEC-003: Residual filter unqualified column resolution ---

    [Fact]
    public void Join_ResidualFilter_UnqualifiedColumn_ResolvesCorrectly()
    {
        // 'amount' exists only in orders — unqualified reference should resolve
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id WHERE amount > 150 ORDER BY o.amount");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_ResidualFilter_AmbiguousColumn_ResolvesToMergedSchema()
    {
        // 'id' exists in both users and orders.  Today the executor resolves
        // unqualified columns against the merged schema which only contains
        // columns that were explicitly materialized.  Because the ON clause
        // requests u.id (not o.id), the merged schema has a single '*.id'
        // entry and the filter resolves to u.id — producing the matching row.
        // Compiler-level ambiguity detection is tracked as a future enhancement.
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name FROM users u JOIN orders o ON u.id = o.user_id WHERE id = 1");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
    }

    [Fact]
    public void Join_ResidualFilter_NonExistentColumn_ReturnsNoMatch()
    {
        // 'no_such_col' doesn't exist — resolves to -1, colVal stays NULL, comparison fails
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT u.name FROM users u JOIN orders o ON u.id = o.user_id WHERE no_such_col = 1");

        // No rows match because the column resolves to NULL
        Assert.False(reader.Read());
    }

    // --- Helpers ---

    private void AddOrphanOrder()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO orders VALUES (99, 99, 999.99)";
        cmd.ExecuteNonQuery();
    }

    private void AddEmptyTable()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS empty_table (id INTEGER PRIMARY KEY, user_id INTEGER)";
        cmd.ExecuteNonQuery();
    }
}
