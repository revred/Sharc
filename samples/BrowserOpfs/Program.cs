// BrowserOpfs Sample — Demonstrates OPFS + JS Interop Bridge Usage
//
// This sample shows the C# patterns for using Sharc in a browser environment
// with OPFS persistent storage. Since OPFS requires a browser runtime, this
// program documents the API patterns and validates the desktop compilation path.
//
// In a real Blazor WASM app, you would use:
//   var opfs = await OpfsPageSource.OpenAsync(jsRuntime, "mydb.sqlite");
//   using var db = SharcDatabase.Open(opfs);

using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using System.Diagnostics;
using System.Runtime.InteropServices;

Console.WriteLine("Sharc Browser OPFS Integration Patterns");
Console.WriteLine("========================================\n");

// --- Pattern 1: Create database in-memory, export to bytes (OPFS write target) ---
Console.WriteLine("Pattern 1: In-Memory → OPFS Export");
Console.WriteLine("-----------------------------------");

var dbPath = Path.Combine(Path.GetTempPath(), "sharc_browser_demo.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE notes (
            id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            body TEXT,
            created_at INTEGER,
            embedding BLOB
        )
        """;
    cmd.ExecuteNonQuery();

    // Insert sample data
    using var tx = conn.BeginTransaction();
    using var insertCmd = conn.CreateCommand();
    insertCmd.CommandText = "INSERT INTO notes (title, body, created_at, embedding) VALUES ($t, $b, $c, $e)";
    insertCmd.Transaction = tx;

    var titleP = insertCmd.Parameters.Add("$t", SqliteType.Text);
    var bodyP = insertCmd.Parameters.Add("$b", SqliteType.Text);
    var createdP = insertCmd.Parameters.Add("$c", SqliteType.Integer);
    var embP = insertCmd.Parameters.Add("$e", SqliteType.Blob);

    var rng = new Random(42);
    for (int i = 0; i < 100; i++)
    {
        titleP.Value = $"Note {i}: {(i % 3 == 0 ? "Meeting" : i % 3 == 1 ? "Idea" : "Task")}";
        bodyP.Value = $"Content for note {i}. This simulates a real note-taking app.";
        createdP.Value = DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeSeconds();

        var vec = new float[32];
        for (int d = 0; d < 32; d++) vec[d] = (float)(rng.NextDouble() * 2 - 1);
        embP.Value = MemoryMarshal.AsBytes(vec.AsSpan()).ToArray();

        insertCmd.ExecuteNonQuery();
    }
    tx.Commit();
}

SqliteConnection.ClearAllPools();
var dbBytes = File.ReadAllBytes(dbPath);
Console.WriteLine($"  Database size: {dbBytes.Length:N0} bytes ({dbBytes.Length / 1024} KB)");
Console.WriteLine("  In browser: await OpfsPageSource.CreateFromBytesAsync(js, 'notes.db', bytes);\n");

// --- Pattern 2: Read from OPFS-backed database ---
Console.WriteLine("Pattern 2: OPFS Read → SharcDatabase");
Console.WriteLine("-------------------------------------");

var sw = Stopwatch.StartNew();
using var db = SharcDatabase.OpenMemory(dbBytes);
sw.Stop();
Console.WriteLine($"  Engine load: {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

sw.Restart();
using var reader = db.CreateReader("notes", "title", "created_at");
int count = 0;
while (reader.Read()) count++;
sw.Stop();
Console.WriteLine($"  Sequential scan: {count} rows in {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- Pattern 3: Write + Upsert (browser write engine) ---
Console.WriteLine("\nPattern 3: Browser Write Engine");
Console.WriteLine("-------------------------------");

var writePath = Path.Combine(Path.GetTempPath(), "sharc_browser_write.db");
File.Copy(dbPath, writePath, true);

using var writer = SharcWriter.Open(writePath);

sw.Restart();
var newTitle = System.Text.Encoding.UTF8.GetBytes("Updated Meeting Notes");
var newBody = System.Text.Encoding.UTF8.GetBytes("Revised agenda for Q1 planning.");
writer.Upsert("notes", 1,
    ColumnValue.FromInt64(4, 1),
    ColumnValue.Text(13 + 2 * newTitle.Length, newTitle),
    ColumnValue.Text(13 + 2 * newBody.Length, newBody),
    ColumnValue.FromInt64(4, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
    ColumnValue.Null());
sw.Stop();
Console.WriteLine($"  Upsert row 1: {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

sw.Restart();
int deleted = writer.DeleteWhere("notes",
    FilterStar.Column("created_at").Lt(DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds()));
sw.Stop();
Console.WriteLine($"  DeleteWhere (older than 60 days): removed {deleted} rows in {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");

// --- Pattern 4: Vector search (RAG-style semantic lookup) ---
Console.WriteLine("\nPattern 4: Vector Search (RAG Semantic Lookup)");
Console.WriteLine("----------------------------------------------");

var queryVec = new float[32];
var queryRng = new Random(99);
for (int d = 0; d < 32; d++) queryVec[d] = (float)(queryRng.NextDouble() * 2 - 1);

sw.Restart();
using var vq = db.Vector("notes", "embedding", DistanceMetric.Cosine);
var results = vq.NearestTo(queryVec, k: 5);
sw.Stop();
Console.WriteLine($"  Top-5 similar notes (Cosine): {sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency:F2} ms");
for (int i = 0; i < results.Count; i++)
    Console.WriteLine($"    #{i + 1}: RowId={results[i].RowId}, Distance={results[i].Distance:F4}");

// --- Pattern 5: Cross-tab coordination (conceptual) ---
Console.WriteLine("\nPattern 5: Cross-Tab Coordination (Browser API)");
Console.WriteLine("------------------------------------------------");
Console.WriteLine("  // In browser:");
Console.WriteLine("  // var lockId = await js.InvokeAsync<string>(\"webLocksBridge.acquireWriteLock\", \"notes.db\");");
Console.WriteLine("  // ... perform writes ...");
Console.WriteLine("  // js.InvokeVoid(\"webLocksBridge.broadcastCommit\", \"notes.db\", dataVersion);");
Console.WriteLine("  // js.InvokeVoid(\"webLocksBridge.releaseLock\", lockId);");

Console.WriteLine("\nDone! All patterns validated.");
