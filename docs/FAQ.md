# FAQ

## General

### What is Sharc?

Sharc is a high-performance, managed reader and writer for the SQLite database file format (Format 3). It is built in pure C# with no native dependencies.

### Why not just use SQLite?

If you need SQL queries, JOINs, GROUP BY, or write-heavy workloads, use SQLite directly. Sharc is for when you need **fast reads** — point lookups in sub-microsecond time, sequential scans 2-4x faster, graph traversal 13.5x faster — without shipping a native DLL.

### Is this production-ready?

The read engine is mature: 1,067 tests pass, benchmarks are honest, and the core pipeline has been extensively tested. The write engine (INSERT only) and trust layer are beta. See [When NOT to Use Sharc](WHEN_NOT_TO_USE.md) for honest limitations.

### Can I write data?

Yes. `SharcWriter` supports INSERT with B-tree splits and ACID transactions. UPDATE and DELETE are planned but not yet implemented.

### What's the trust layer?

A built-in cryptographic audit system. Agents register with public keys, sign data entries, and append to a tamper-evident hash-chain ledger. When an AI agent makes a recommendation, you can verify who contributed the underlying data and whether it's been modified. See [Cookbook Recipe 15](COOKBOOK.md#15-register-agent-append-ledger-verify-chain).

## Technical

### Why is Sharc faster than standard SQLite?

Sharc eliminates the SQL parser, the VDBE bytecode engine, and the P/Invoke boundary. It walks B-tree pages directly in managed memory using `ReadOnlySpan<byte>`, so bytes go from the page to your variable with zero intermediate object allocations.

### Does Sharc support WAL mode?

Yes. Sharc can read SQLite databases in WAL (Write-Ahead Log) mode by merging the main database file with the `-wal` file in memory.

### Does it support WITHOUT ROWID tables?

Yes. Sharc supports both standard rowid tables and `WITHOUT ROWID` tables, including high-speed seeking on primary keys.

### Can I use this in Blazor WASM?

Yes. Sharc is optimized for WebAssembly. It is ~40x smaller than the standard SQLite WASM bundle and operates entirely within the managed runtime.

### How do I handle concurrent access?

Sharc supports multiple parallel readers. Write support currently follows a single-writer model.

### How does Sharc compare to DuckDB?

Different tools for different jobs. DuckDB is a columnar OLAP engine — excellent for analytics, aggregation, and large-scale scans. Sharc is a row-oriented engine optimized for point lookups and AI context delivery. Use DuckDB for analytics; use Sharc for fast key-value reads and graph traversal.

### How does Sharc compare to LiteDB?

LiteDB is a document database with its own file format. Sharc reads the standard SQLite format, which means any tool that exports to SQLite is compatible with Sharc. Sharc is faster for reads; LiteDB has richer write patterns and a document model.

## Troubleshooting

### Why am I getting an `InvalidDatabaseException`?

The file is not a valid SQLite Format 3 database. Ensure your file starts with the header `SQLite format 3\0`. This exception also fires for unsupported format versions.

### `CorruptPageException` during reads?

A B-tree page has invalid structure — bad page type, pointer out of bounds, or cell overflow. This usually means the database file is truncated or corrupted.

### Column index out of range?

If you use column projection (`db.CreateReader("table", "col1", "col2")`), you can only access columns 0 and 1. The ordinal is the position in the projection, not the position in the original table schema.
