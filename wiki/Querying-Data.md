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

## Supported Syntax

Sharq supports:
- `SELECT` with column projection
- `WHERE` with comparison operators (`=`, `!=`, `<`, `>`, `<=`, `>=`)
- `AND` / `OR` logical operators
- Parameterized values (`$param`)
- `JOIN` (inner joins via the query pipeline)
- `ORDER BY` (via cursor-based sorting)
- Table and column name resolution (case-insensitive)
