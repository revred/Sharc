using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using System.Diagnostics;

Console.WriteLine("Sharc Bulk Insert Sample");
Console.WriteLine("------------------------");

// --- Create a database with schema using SQLite ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_insert.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE logs (id INTEGER PRIMARY KEY, message TEXT, level INTEGER)";
    cmd.ExecuteNonQuery();
}

// --- Insert rows using SharcWriter ---
var sw = Stopwatch.StartNew();

using var writer = SharcWriter.Open(dbPath);
using var tx = writer.BeginTransaction();

for (int i = 0; i < 100; i++)
{
    var msg = System.Text.Encoding.UTF8.GetBytes($"Log entry {i}");
    tx.Insert("logs",
        ColumnValue.FromInt64(4, i),
        ColumnValue.Text(13 + 2 * msg.Length, msg),
        ColumnValue.FromInt64(1, i % 5));
}

tx.Commit();
sw.Stop();

Console.WriteLine($"Inserted 100 rows in {sw.ElapsedMilliseconds} ms.");

// --- Verify by reading back ---
using var db = SharcDatabase.Open(dbPath);
using var reader = db.CreateReader("logs");

int count = 0;
while (reader.Read()) count++;
Console.WriteLine($"Verified: {count} rows in table.");
