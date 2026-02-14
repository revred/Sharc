using Sharc;
using System.Diagnostics;

Console.WriteLine("Sharc Bulk Insert (Write Engine Phase 1) Sample");
Console.WriteLine("-----------------------------------------------");

string dbPath = "output_data.db";

// Ensure a fresh database for the sample
if (File.Exists(dbPath)) File.Delete(dbPath);

var sw = Stopwatch.StartNew();

// Phase 1 Write Support: Manual B-tree mutation
// This is used for high-performance direct page writes.
// A simpler db.Insert(object) API is planned for Phase 2.
using var db = SharcDatabase.Create(dbPath);
using var writer = db.CreateWriter("logs");

for (int i = 0; i < 1000; i++)
{
    writer.Insert([i, $"Log entry {i}", DateTime.UtcNow.ToString("o")]);
    
    if (i % 100 == 0) Console.WriteLine($"Inserted {i} rows...");
}

sw.Stop();

Console.WriteLine($"-----------------------------------------------");
Console.WriteLine($"SUCCESS: Inserted 1,000 rows in {sw.ElapsedMilliseconds} ms.");
Console.WriteLine($"Average: {sw.Elapsed.TotalMicroseconds / 1000:F2} us per insert (including I/O).");
