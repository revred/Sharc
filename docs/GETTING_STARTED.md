# Getting Started with Sharc

Sharc is a high-performance SQLite format reader for .NET. This guide will get you up and running in under 5 minutes.

## 1. Installation

Install the core package via NuGet:

```bash
dotnet add package Sharc
```

## 2. Basic Reading

Open a database and read all rows from a table. Sharc handles the B-tree traversal automatically.

```csharp
using Sharc;

using var db = SharcDatabase.Open("data.db");
using var reader = db.CreateReader("users");

while (reader.Read())
{
    Console.WriteLine($"User: {reader.GetString("name")} (ID: {reader.GetInt64("id")})");
}
```

## 3. High-Speed Point Lookups (Seek)

If your table has a Primary Key or a unique index, use `Seek` for O(log N) performance. This is **40-60x faster** than standard SQLite queries.

```csharp
using var reader = db.CreateReader("users");

// Seek directly to a row by ID in < 1 microsecond
if (reader.Seek(1234))
{
    Console.WriteLine($"Found: {reader.GetString("email")}");
}
```

## 4. Efficient Filtering

Filter rows without loading them into memory. Sharc's zero-allocation filter engine (FilterStar) executes directly on the page data.

```csharp
var filtered = db.CreateReader("orders")
                .Where("status", FilterOp.Equals, "pending")
                .Where("amount", FilterOp.GreaterThan, 500.0);

while (filtered.Read())
{
    Console.WriteLine($"Large Pending Order: {filtered.GetInt64("order_id")}");
}
```

## 5. Summary of Built-in Layers

- **Core**: High-speed B-tree navigation and record decoding.
- **Crypto**: `Sharc.Crypto` for AES-256-GCM encrypted databases.
- **Graph**: `Sharc.Graph` for relationship traversal and trust-based auditing.

[View Full Cookbook](COOKBOOK.md) | [Benchmarks](BENCHMARKS.md)
