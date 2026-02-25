# Querying Data

## Sharq Query Language

Sharc supports a SQL-like query language called Sharq for structured data access:

```csharp
using var db = SharcDatabase.Open("mydata.db");

using var reader = db.Query("SELECT id, name FROM users WHERE age > 18");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
}
```

## Parameterized Queries

Prevent injection and enable plan caching with parameters:

```csharp
var parameters = new Dictionary<string, object>
{
    ["$minAge"] = 18L,
    ["$status"] = "active"
};

using var reader = db.Query(parameters,
    "SELECT id, name FROM users WHERE age > $minAge AND status = $status");
```

## Agent-Scoped Queries

Enforce read entitlements by passing an `AgentInfo`:

```csharp
// Agent can only read tables/columns allowed by their ReadScope
using var reader = db.Query("SELECT * FROM sensitive_data", agent);
// Throws UnauthorizedAccessException if ReadScope denies access
```

See [Trust Layer](Trust-Layer) for agent setup.

## Query Plan Caching

Sharc caches compiled query plans (filter delegates, projection arrays, table metadata) for repeated queries with the same structure. The cache is keyed by `QueryIntent`, so parameterized queries benefit from plan reuse automatically.

## Streaming Top-K with Custom Scoring

Use `JitQuery.TopK()` to find the K best-scoring rows without materializing the entire result set. The scorer runs on each row after all filters have been applied. Rows that score worse than the current worst in the bounded heap are never materialized, keeping memory at O(K).

Lower scores rank higher (natural for distance-based scoring).

### With a reusable scorer class

```csharp
// Implement IRowScorer for reusable, stateful scoring
sealed class DistanceScorer(double cx, double cy) : IRowScorer
{
    public double Score(IRowAccessor row)
    {
        double dx = row.GetDouble(0) - cx;
        double dy = row.GetDouble(1) - cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

// Filter first (B-tree accelerated), then score survivors
var jit = db.Jit("points");
jit.Where(FilterStar.Column("x").Between(cx - r, cx + r));
jit.Where(FilterStar.Column("y").Between(cy - r, cy + r));
using var reader = jit.TopK(20, new DistanceScorer(cx, cy), "x", "y", "id");

while (reader.Read())
{
    double x = reader.GetDouble(0);
    double y = reader.GetDouble(1);
    long id = reader.GetInt64(2);
}
```

### With a lambda scorer

```csharp
// One-off scoring without implementing IRowScorer
using var reader = jit.TopK(10,
    row => Math.Abs(row.GetDouble(0) - target),
    "value", "label");
```

### Key points

- `TopK()` is a **terminal method** (like `Query()`) that executes immediately
- Composes with all existing filters: `FilterStar`, `IRowAccessEvaluator`, `WithLimit`
- Only K rows are materialized; rejected rows incur zero allocation
- Results are sorted ascending by score (best first)

See the [`PrimeExample`](../samples/PrimeExample/) sample for a complete spatial query walkthrough.

## Supported Syntax

Sharq supports:
- `SELECT` with column projection
- `WHERE` with comparison operators (`=`, `!=`, `<`, `>`, `<=`, `>=`)
- `AND` / `OR` logical operators
- Parameterized values (`$param`)
- `JOIN` (inner joins via the query pipeline)
- `ORDER BY` (via cursor-based sorting)
- `TopK` with custom scoring (via `JitQuery.TopK()`)
- Table and column name resolution (case-insensitive)
