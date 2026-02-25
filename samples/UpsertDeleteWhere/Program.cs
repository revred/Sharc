using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using System.Diagnostics;

Console.WriteLine("Sharc Upsert & DeleteWhere Sample");
Console.WriteLine("==================================");

// --- Create a database with schema using SQLite ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_upsert_sample.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE products (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            price REAL,
            category TEXT,
            stock INTEGER
        )
        """;
    cmd.ExecuteNonQuery();
}

// --- Seed initial data ---
using var writer = SharcWriter.Open(dbPath);
var categories = new[] { "electronics", "clothing", "food", "books", "toys" };

for (int i = 1; i <= 50; i++)
{
    var nameBytes = System.Text.Encoding.UTF8.GetBytes($"Product-{i}");
    var catBytes = System.Text.Encoding.UTF8.GetBytes(categories[(i - 1) % 5]);
    writer.Insert("products",
        ColumnValue.FromInt64(4, i),
        ColumnValue.Text(13 + 2 * nameBytes.Length, nameBytes),
        ColumnValue.FromDouble(9.99 + i * 1.50),
        ColumnValue.Text(13 + 2 * catBytes.Length, catBytes),
        ColumnValue.FromInt64(2, 100 - i));
}

Console.WriteLine("Inserted 50 products.");

// --- Upsert: update existing row 5 ---
var sw = Stopwatch.StartNew();
var updatedName = System.Text.Encoding.UTF8.GetBytes("Premium-Widget");
var updatedCat = System.Text.Encoding.UTF8.GetBytes("electronics");
writer.Upsert("products", 5,
    ColumnValue.FromInt64(4, 5),
    ColumnValue.Text(13 + 2 * updatedName.Length, updatedName),
    ColumnValue.FromDouble(99.99),
    ColumnValue.Text(13 + 2 * updatedCat.Length, updatedCat),
    ColumnValue.FromInt64(2, 500));
sw.Stop();
Console.WriteLine($"Upsert (update existing row 5): {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- Upsert: insert new row 100 ---
sw.Restart();
var newName = System.Text.Encoding.UTF8.GetBytes("New-Gadget");
var newCat = System.Text.Encoding.UTF8.GetBytes("electronics");
writer.Upsert("products", 100,
    ColumnValue.FromInt64(4, 100),
    ColumnValue.Text(13 + 2 * newName.Length, newName),
    ColumnValue.FromDouble(49.99),
    ColumnValue.Text(13 + 2 * newCat.Length, newCat),
    ColumnValue.FromInt64(2, 250));
sw.Stop();
Console.WriteLine($"Upsert (insert new row 100): {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- DeleteWhere: remove all 'toys' category ---
sw.Restart();
int deleted = writer.DeleteWhere("products",
    FilterStar.Column("category").Eq("toys"));
sw.Stop();
Console.WriteLine($"DeleteWhere (category='toys'): removed {deleted} rows in {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- DeleteWhere: remove products with stock < 60 ---
sw.Restart();
int deleted2 = writer.DeleteWhere("products",
    FilterStar.Column("stock").Lt(60));
sw.Stop();
Console.WriteLine($"DeleteWhere (stock < 60): removed {deleted2} rows in {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- Verify final state ---
using var db = writer.Database;
using var reader = db.CreateReader("products");
int finalCount = 0;
while (reader.Read()) finalCount++;
Console.WriteLine($"\nFinal row count: {finalCount}");
Console.WriteLine("Done!");
