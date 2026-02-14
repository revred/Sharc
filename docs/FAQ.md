# FAQ

## General

### What is Sharc?
Sharc is a high-performance, managed reader and writer for the SQLite database file format (Format 3). It is built in pure C# with no native dependencies.

### Why is it faster than standard SQLite?
Sharc eliminates the SQL parser, the VDBE bytecode engine, and the P/Invoke boundary. It walks the B-tree pages directly in managed memory using `ReadOnlySpan<byte>`, meaning the bytes go from the page to your variable with zero intermediate object allocations.

### Is it a full replacement for SQLite?
**No.** Sharc is a specialized read/write engine optimized for point lookups and sequential scans. It does not support JOINs, GROUP BY, or arbitrary SQL queries. See [When Not To Use Sharc](WHEN_NOT_TO_USE.md).

## Technical

### Does Sharc support WAL mode?
Yes. Sharc can read SQLite databases in WAL (Write-Ahead Log) mode by merging the main database file with the `-wal` file in memory.

### Does it support WITHOUT ROWID tables?
Yes. Sharc supports both standard rowid tables and `WITHOUT ROWID` tables, including high-speed seeking on primary keys.

### Can I use it in the browser?
Absolutely. Sharc is optimized for WebAssembly (Blazor). It is 40x smaller than the standard SQLite WASM bundle and significantly faster because it operates entirely within the managed runtime.

### How do I handle concurrent access?
Sharc supports multiple parallel readers. Write support (Phase 1) currently follows a single-writer model.

## Troubleshooting

### Why am I getting a 'Page not found' error?
This usually means the database file is corrupted or is not a valid SQLite Format 3 file. Ensure your file starts with the header: `SQLite format 3\0`.

### 'Column not found' inside a reader?
Verify your column project. If you call `db.CreateReader("table", "col1")`, you cannot access `col2` from that reader.
