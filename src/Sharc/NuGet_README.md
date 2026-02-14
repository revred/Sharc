# Sharc

**Read SQLite files 2-56x faster than Microsoft.Data.Sqlite, in pure C#, with zero native dependencies.**

Sharc is a high-performance, zero-allocation SQLite format reader and writer designed for AI context space engineering, edge computing, and high-throughput point lookups.

## Key Features

- **Extreme Performance**: Up to 56x faster than standard P/Invoke-based readers for B-tree seeks.
- **Zero Allocation**: Hot paths use `ReadOnlySpan<byte>` and `stackalloc` to eliminate GC pressure.
- **Pure C#**: No native DLLs. Runs anywhere .NET runs (Windows, Linux, macOS, WASM, IoT).
- **Sub-50KB Footprint**: 40x smaller than the standard SQLite bundle.
- **Cryptographic Trust**: Built-in support for signed, tamper-evident data ledgers.
- **Encryption**: Production-grade AES-256-GCM + Argon2id support (via `Sharc.Crypto`).

## Quick Start

```csharp
using Sharc;

// Open a database (zero-allocation read)
using var db = SharcDatabase.Open("project_context.db");

// Seek a row by Primary Key in < 600ns
using var reader = db.CreateReader("commits");
if (reader.ReadByRowId(1234))
{
    Console.WriteLine($"Message: {reader.GetString("message")}");
}

// Scan with high-performance filters
var filtered = db.CreateReader("files", "path", "size")
                .Where("size", FilterOp.GreaterThan, 1024);

while (filtered.Read())
{
    Console.WriteLine(filtered.GetString(0));
}
```

## When to Use Sharc

Sharc is optimized for **Point Lookups**, **Structured AI Context**, and **Embedded Scenarios**. It is a complement to SQLite, not a full replacement. Use Sharc when performance, allocation, or zero-dependency deployment is critical.

[Full Documentation & Benchmarks](https://github.com/revred/Sharc)
