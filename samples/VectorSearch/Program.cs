using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using System.Diagnostics;
using System.Runtime.InteropServices;

Console.WriteLine("Sharc Vector Similarity Search Sample");
Console.WriteLine("======================================");

// --- Create a database with vector embeddings using SQLite ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_vector_sample.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

const int rowCount = 1000;
const int dimensions = 64;
var rng = new Random(42);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE documents (
            id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            category TEXT,
            embedding BLOB
        )
        """;
    cmd.ExecuteNonQuery();

    // Insert documents with random vector embeddings
    var categories = new[] { "science", "tech", "art", "history", "finance" };

    using var tx = conn.BeginTransaction();
    using var insertCmd = conn.CreateCommand();
    insertCmd.CommandText = "INSERT INTO documents (title, category, embedding) VALUES ($t, $c, $e)";
    insertCmd.Transaction = tx;

    var titleParam = insertCmd.Parameters.Add("$t", SqliteType.Text);
    var catParam = insertCmd.Parameters.Add("$c", SqliteType.Text);
    var embParam = insertCmd.Parameters.Add("$e", SqliteType.Blob);

    for (int i = 0; i < rowCount; i++)
    {
        titleParam.Value = $"Document-{i}";
        catParam.Value = categories[i % categories.Length];

        // Generate random embedding and encode as BLOB
        var vec = new float[dimensions];
        for (int d = 0; d < dimensions; d++)
            vec[d] = (float)(rng.NextDouble() * 2 - 1);

        embParam.Value = MemoryMarshal.AsBytes(vec.AsSpan()).ToArray();
        insertCmd.ExecuteNonQuery();
    }
    tx.Commit();
}

Console.WriteLine($"Created {rowCount} documents × {dimensions} dimensions.\n");

// --- Open with Sharc ---
var dbBytes = File.ReadAllBytes(dbPath);
using var db = SharcDatabase.OpenMemory(dbBytes);

// Generate a query vector
var queryVec = new float[dimensions];
var queryRng = new Random(99);
for (int d = 0; d < dimensions; d++)
    queryVec[d] = (float)(queryRng.NextDouble() * 2 - 1);

// --- 1. Cosine similarity: find top-5 nearest neighbors ---
var sw = Stopwatch.StartNew();
using var vq = db.Vector("documents", "embedding", DistanceMetric.Cosine);
var results = vq.NearestTo(queryVec, k: 5);
sw.Stop();

Console.WriteLine($"Top-5 Nearest Neighbors (Cosine) — {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F3} ms");
for (int i = 0; i < results.Count; i++)
    Console.WriteLine($"  #{i + 1}: RowId={results[i].RowId}, Distance={results[i].Distance:F4}");

// --- 2. Filtered vector search: only 'science' documents ---
sw.Restart();
using var vq2 = db.Vector("documents", "embedding", DistanceMetric.Cosine);
vq2.Where(FilterStar.Column("category").Eq("science"));
var filtered = vq2.NearestTo(queryVec, k: 5);
sw.Stop();

Console.WriteLine($"\nTop-5 Nearest (Cosine, category='science') — {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F3} ms");
for (int i = 0; i < filtered.Count; i++)
    Console.WriteLine($"  #{i + 1}: RowId={filtered[i].RowId}, Distance={filtered[i].Distance:F4}");

// --- 3. Euclidean distance ---
sw.Restart();
using var vq3 = db.Vector("documents", "embedding", DistanceMetric.Euclidean);
var euclidean = vq3.NearestTo(queryVec, k: 3);
sw.Stop();

Console.WriteLine($"\nTop-3 Nearest (Euclidean) — {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F3} ms");
for (int i = 0; i < euclidean.Count; i++)
    Console.WriteLine($"  #{i + 1}: RowId={euclidean[i].RowId}, Distance={euclidean[i].Distance:F4}");

// --- 4. Dot Product ---
sw.Restart();
using var vq4 = db.Vector("documents", "embedding", DistanceMetric.DotProduct);
var dotprod = vq4.NearestTo(queryVec, k: 3);
sw.Stop();

Console.WriteLine($"\nTop-3 Nearest (DotProduct) — {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F3} ms");
for (int i = 0; i < dotprod.Count; i++)
    Console.WriteLine($"  #{i + 1}: RowId={dotprod[i].RowId}, Distance={dotprod[i].Distance:F4}");

Console.WriteLine("\nDone!");
