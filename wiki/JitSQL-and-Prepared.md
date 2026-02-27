# JitSQL & Prepared Queries

## JitQuery

JitQuery compiles a SQL-like expression into a reusable, zero-allocation query plan:

```csharp
using var jit = db.JitQuery("SELECT id, name FROM users WHERE age > @age", new { age = 25L });
while (jit.Read())
    Console.WriteLine($"{jit.GetInt64(0)}: {jit.GetString(1)}");
```

## PreparedReader

PreparedReader provides zero-allocation cursor reuse for repeated point lookups:

```csharp
using var prepared = db.PrepareReader("users");

// Seek by primary key — reuses the same cursor, zero allocation per seek
if (prepared.Seek(42))
    Console.WriteLine(prepared.GetString(1));

if (prepared.Seek(99))
    Console.WriteLine(prepared.GetString(1));
```

## Execution Hints

| Hint | Effect |
|:---|:---|
| `CACHED` | Cache the compiled query plan for reuse |
| `JIT` | Use JIT-compiled execution path |

```csharp
using var results = db.Query("/*+ CACHED */ SELECT * FROM users WHERE active = 1");
```

## FilterStar

`FilterStar` provides predicate-based filtering with column projection:

```csharp
using var reader = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 18L));
```

## See Also

- [Reading Data](Reading-Data) — Core reader API
- [Performance Guide](Performance-Guide) — Zero-allocation patterns
- [AI Agent Reference](AI-Agent-Reference) — Copy-paste patterns for LLM assistants
