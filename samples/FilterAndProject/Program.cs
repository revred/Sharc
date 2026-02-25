using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core.Query;

// --- Generate test database ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_filter.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER, status TEXT)";
    cmd.ExecuteNonQuery();

    using var tx = conn.BeginTransaction();
    cmd.CommandText = "INSERT INTO users VALUES ($id, $name, $age, $status)";
    cmd.Transaction = tx;
    var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
    var pAge = cmd.Parameters.Add("$age", SqliteType.Integer);
    var pStatus = cmd.Parameters.Add("$status", SqliteType.Text);
    string[] statuses = ["active", "inactive", "suspended"];
    for (int i = 1; i <= 1000; i++)
    {
        pId.Value = i;
        pName.Value = $"User_{i}";
        pAge.Value = 15 + (i % 70);
        pStatus.Value = statuses[i % 3];
        cmd.ExecuteNonQuery();
    }
    tx.Commit();
}

// --- Filter and project with Sharc ---
Console.WriteLine("Sharc Filter & Project Sample");
Console.WriteLine("-----------------------------");

using var db = SharcDatabase.Open(dbPath);

// Simple filter: age >= 60
Console.WriteLine("\n--- SharcFilter: age >= 60 ---");
using var reader1 = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 60L));

int count = 0;
while (reader1.Read())
{
    Console.WriteLine($"[{reader1.GetInt64(0)}] {reader1.GetString(1)}, age {reader1.GetInt64(2)}");
    count++;
}
Console.WriteLine($"Matched {count} rows.");

// Composable filter with FilterStar: active users aged 21-30
Console.WriteLine("\n--- FilterStar: active AND age 21-30 ---");
var filter = FilterStar.And(
    FilterStar.Column("status").Eq("active"),
    FilterStar.Column("age").Between(21L, 30L)
);

// Project only 'name' and 'age' columns
using var reader2 = db.CreateReader("users", ["name", "age"], filter);

count = 0;
while (reader2.Read())
{
    Console.WriteLine($"{reader2.GetString(0)}, age {reader2.GetInt64(1)}");
    count++;
}
Console.WriteLine($"Matched {count} rows.");
