// PrimeExample — Streaming TopK with Custom Scoring
//
// Demonstrates how a project like PrimeSpiral can use Sharc's JitQuery.TopK()
// to find the K nearest points to a target coordinate without materializing
// the entire result set. Filters narrow the search space first, then the
// scorer ranks only the surviving rows.

using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Views;

// ── 1. Generate a spatial dataset ────────────────────────────────────────────
// Simulate 10,000 prime-spiral points with (x, y) coordinates and an arm label.

var dbPath = Path.Combine(Path.GetTempPath(), "sharc_prime_example.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE points (
            id   INTEGER PRIMARY KEY,
            x    REAL,
            y    REAL,
            arm  TEXT
        )
        """;
    cmd.ExecuteNonQuery();

    // Insert 10,000 spiral-like points
    using var tx = conn.BeginTransaction();
    cmd.Transaction = tx;
    string[] arms = ["alpha", "beta", "gamma", "delta"];
    var rng = new Random(42);
    for (int i = 0; i < 10_000; i++)
    {
        double angle = i * 0.1;
        double r = 0.5 + i * 0.02 + rng.NextDouble() * 2;
        double x = Math.Round(Math.Cos(angle) * r, 4);
        double y = Math.Round(Math.Sin(angle) * r, 4);
        string arm = arms[i % 4];
        cmd.CommandText = $"INSERT INTO points (x, y, arm) VALUES ({x}, {y}, '{arm}')";
        cmd.ExecuteNonQuery();
    }
    tx.Commit();
}

Console.WriteLine("PrimeExample: Streaming TopK with Custom Scoring");
Console.WriteLine("================================================\n");

using var db = SharcDatabase.Open(dbPath);

// ── 2. Basic TopK: 10 nearest points to a target ────────────────────────────
// No filters — just score all 10,000 points by Euclidean distance.

double targetX = 50.0, targetY = 30.0;
Console.WriteLine($"Target: ({targetX}, {targetY})\n");

Console.WriteLine("--- Top 10 nearest points (no filter) ---");
var jit = db.Jit("points");
using var nearest = jit.TopK(10, new DistanceScorer(targetX, targetY), "id", "x", "y", "arm");

while (nearest.Read())
{
    double dx = nearest.GetDouble(1) - targetX;
    double dy = nearest.GetDouble(2) - targetY;
    double dist = Math.Sqrt(dx * dx + dy * dy);
    Console.WriteLine($"  id={nearest.GetInt64(0),5}  ({nearest.GetDouble(1),8:F2}, {nearest.GetDouble(2),8:F2})  arm={nearest.GetString(3),-6}  dist={dist:F4}");
}

// ── 3. Filtered TopK: spatial bounding box + arm filter ─────────────────────
// First narrow with FilterStar (B-tree acceleration), then score survivors.
// This is the pattern PrimeSpiral would use: space partition first, then rank.

Console.WriteLine("\n--- Top 5 nearest 'alpha' points within bounding box ---");
double radius = 80.0;
jit.ClearFilters();
jit.Where(FilterStar.Column("x").Between(targetX - radius, targetX + radius));
jit.Where(FilterStar.Column("y").Between(targetY - radius, targetY + radius));
jit.Where(FilterStar.Column("arm").Eq("alpha"));

using var filtered = jit.TopK(5, new DistanceScorer(targetX, targetY), "id", "x", "y");

while (filtered.Read())
{
    double dx = filtered.GetDouble(1) - targetX;
    double dy = filtered.GetDouble(2) - targetY;
    double dist = Math.Sqrt(dx * dx + dy * dy);
    Console.WriteLine($"  id={filtered.GetInt64(0),5}  ({filtered.GetDouble(1),8:F2}, {filtered.GetDouble(2),8:F2})  dist={dist:F4}");
}

// ── 4. Lambda scorer: weighted distance ─────────────────────────────────────
// For one-off scoring, use a lambda instead of implementing IRowScorer.

Console.WriteLine("\n--- Top 5 by weighted distance (x weight=2, y weight=1) ---");
jit.ClearFilters();
using var weighted = jit.TopK(5,
    row =>
    {
        double dx = row.GetDouble(1) - targetX;  // ordinal 1 = x (after projection)
        double dy = row.GetDouble(2) - targetY;  // ordinal 2 = y
        return Math.Sqrt(4 * dx * dx + dy * dy);  // x-axis weighted 2x
    },
    "id", "x", "y");

while (weighted.Read())
{
    Console.WriteLine($"  id={weighted.GetInt64(0),5}  ({weighted.GetDouble(1),8:F2}, {weighted.GetDouble(2),8:F2})");
}

// ── 5. Reusable scorer class ────────────────────────────────────────────────
// For repeated queries with different targets, implement IRowScorer once.

Console.WriteLine("\n--- Reusable scorer: 3 nearest to (0, 0) then (-20, 15) ---");
var scorer1 = new DistanceScorer(0, 0);
var scorer2 = new DistanceScorer(-20, 15);

jit.ClearFilters();
using var near1 = jit.TopK(3, scorer1, "id", "x", "y");
Console.WriteLine("  Near (0,0):");
while (near1.Read())
    Console.WriteLine($"    id={near1.GetInt64(0),5}  ({near1.GetDouble(1),8:F2}, {near1.GetDouble(2),8:F2})");

jit.ClearFilters();
using var near2 = jit.TopK(3, scorer2, "id", "x", "y");
Console.WriteLine("  Near (-20,15):");
while (near2.Read())
    Console.WriteLine($"    id={near2.GetInt64(0),5}  ({near2.GetDouble(1),8:F2}, {near2.GetDouble(2),8:F2})");

Console.WriteLine("\nDone.");

// Cleanup
try { File.Delete(dbPath); } catch { }

// ═════════════════════════════════════════════════════════════════════════════
// Scorer implementation — reusable across queries
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Euclidean distance scorer. Lower distance = better score.
/// Ordinals 1 and 2 correspond to x and y after projection (id=0, x=1, y=2).
/// </summary>
sealed class DistanceScorer(double cx, double cy) : IRowScorer
{
    public double Score(IRowAccessor row)
    {
        double dx = row.GetDouble(1) - cx;  // x column (ordinal 1 in projected output)
        double dy = row.GetDouble(2) - cy;  // y column (ordinal 2 in projected output)
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
