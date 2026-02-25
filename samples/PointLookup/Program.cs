using Microsoft.Data.Sqlite;
using Sharc;
using System.Diagnostics;

// --- Generate test database with 5,000 rows ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_seek.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)";
    cmd.ExecuteNonQuery();

    using var tx = conn.BeginTransaction();
    cmd.CommandText = "INSERT INTO users VALUES ($id, $name, $age)";
    cmd.Transaction = tx;
    var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
    var pAge = cmd.Parameters.Add("$age", SqliteType.Integer);
    for (int i = 1; i <= 5000; i++)
    {
        pId.Value = i;
        pName.Value = $"User_{i}";
        pAge.Value = 18 + (i % 60);
        cmd.ExecuteNonQuery();
    }
    tx.Commit();
}

// --- Point lookup with Sharc ---
Console.WriteLine("Sharc Point Lookup (Seek) Sample");
Console.WriteLine("--------------------------------");

using var db = SharcDatabase.Open(dbPath);
using var reader = db.CreateReader("users");

long targetId = 2500;
var sw = Stopwatch.StartNew();
bool found = reader.Seek(targetId);
sw.Stop();

if (found)
{
    Console.WriteLine($"Found user {targetId} in {sw.Elapsed.TotalMicroseconds:F2} us");
    Console.WriteLine($"Name: {reader.GetString(1)}, Age: {reader.GetInt64(2)}");
}
else
{
    Console.WriteLine($"User {targetId} not found.");
}
