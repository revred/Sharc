using Microsoft.Data.Sqlite;
using Sharc;

// --- Generate test database ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_basic.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT);
        INSERT INTO users VALUES (1, 'Alice', 'alice@example.com');
        INSERT INTO users VALUES (2, 'Bob', 'bob@example.com');
        INSERT INTO users VALUES (3, 'Charlie', 'charlie@example.com');
        INSERT INTO users VALUES (4, 'Diana', 'diana@example.com');
        INSERT INTO users VALUES (5, 'Eve', 'eve@example.com');
        """;
    cmd.ExecuteNonQuery();
}

// --- Read with Sharc ---
Console.WriteLine("Sharc Basic Read Sample");
Console.WriteLine("-----------------------");

using var db = SharcDatabase.Open(dbPath);
Console.WriteLine($"Database: {db.Schema.Tables.Count} table(s) found.");

// Column projection: only load 'id' and 'name'
using var reader = db.CreateReader("users", "id", "name");

int rowCount = 0;
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    Console.WriteLine($"[{id}] {name}");
    rowCount++;
}

Console.WriteLine($"-----------------------");
Console.WriteLine($"Read {rowCount} rows.");
