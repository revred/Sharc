# SQLite C Source Analysis — Sharc Reader Digest

## 1. SQLite Database File Format (Format 3)

### 1.1 File Header (First 100 bytes)

| Offset | Size | Description |
|--------|------|-------------|
| 0      | 16   | Magic string: `"SQLite format 3\000"` |
| 16     | 2    | Page size (power of 2, 512–65536; value 1 = 65536) |
| 18     | 1    | File format write version (1=legacy, 2=WAL) |
| 19     | 1    | File format read version (1=legacy, 2=WAL) |
| 20     | 1    | Reserved bytes per page (usually 0) |
| 21     | 1    | Max embedded payload fraction (must be 64) |
| 22     | 1    | Min embedded payload fraction (must be 32) |
| 23     | 1    | Leaf payload fraction (must be 32) |
| 24     | 4    | File change counter |
| 28     | 4    | Database size in pages |
| 32     | 4    | First freelist trunk page |
| 36     | 4    | Total freelist pages |
| 40     | 4    | Schema cookie |
| 44     | 4    | Schema format number (1–4) |
| 48     | 4    | Default page cache size |
| 52     | 4    | Largest root b-tree page (auto-vacuum/incremental) |
| 56     | 4    | Database text encoding (1=UTF-8, 2=UTF-16le, 3=UTF-16be) |
| 60     | 4    | User version |
| 64     | 4    | Incremental vacuum mode |
| 68     | 4    | Application ID |
| 72     | 20   | Reserved for expansion (must be zero) |
| 92     | 4    | Version-valid-for number |
| 96     | 4    | SQLite version number |

### 1.2 Page Structure

Every page is `page_size` bytes. Page 1 starts at file offset 0, page 2 at `page_size`, etc.
Page 1 is special: the first 100 bytes are the file header; the b-tree header follows at offset 100.

### 1.3 B-Tree Page Types

| Value | Type |
|-------|------|
| 0x02  | Interior index b-tree |
| 0x05  | Interior table b-tree |
| 0x0A  | Leaf index b-tree |
| 0x0D  | Leaf table b-tree |

#### B-Tree Page Header

| Offset | Size | Description |
|--------|------|-------------|
| 0      | 1    | Page type flag |
| 1      | 2    | First freeblock offset |
| 3      | 2    | Cell count |
| 5      | 2    | Cell content area start |
| 7      | 1    | Fragmented free bytes |
| 8      | 4    | Right-most pointer (interior pages only) |

Cell pointer array follows the header (2 bytes per cell, big-endian).

### 1.4 Varint Encoding

SQLite uses a variable-length integer encoding (1–9 bytes):
- Bytes 1–8: high bit = continuation flag, low 7 bits = data
- Byte 9 (if present): all 8 bits are data
- Maximum value: 64-bit signed integer

### 1.5 Record Format (Serial Types)

Each record has a header followed by column values:
- Header: varint total header size, then per-column serial type varints
- Serial types determine storage class and byte length:

| Serial Type | Meaning | Size |
|-------------|---------|------|
| 0           | NULL    | 0    |
| 1           | 8-bit int | 1 |
| 2           | 16-bit int (BE) | 2 |
| 3           | 24-bit int (BE) | 3 |
| 4           | 32-bit int (BE) | 4 |
| 5           | 48-bit int (BE) | 6 |
| 6           | 64-bit int (BE) | 8 |
| 7           | IEEE 754 float (BE) | 8 |
| 8           | Integer 0 | 0 |
| 9           | Integer 1 | 0 |
| 10,11       | Reserved | - |
| ≥12, even   | BLOB, length = (N-12)/2 | variable |
| ≥12, odd    | TEXT, length = (N-13)/2 | variable |

### 1.6 sqlite_schema Table

Page 1 root b-tree contains the schema. Each row:
- `type` (TEXT): "table", "index", "view", "trigger"
- `name` (TEXT): object name
- `tbl_name` (TEXT): associated table name
- `rootpage` (INTEGER): root b-tree page number
- `sql` (TEXT): CREATE statement

## 2. Key Read Paths in SQLite C Source

### 2.1 Pager (`pager.c`)
- Manages page I/O, caching, and locking
- `sqlite3PagerGet()` — fetch page by number, hits cache first
- Pages are reference-counted; read-only path only needs shared lock
- **Sharc equivalent**: `IPageSource` with LRU cache

### 2.2 B-Tree (`btree.c`)
- `sqlite3BtreeOpen()` — init b-tree on pager
- `sqlite3BtreeCursor()` — open cursor on a specific root page
- `sqlite3BtreeFirst()` / `sqlite3BtreeNext()` — sequential scan
- `sqlite3BtreePayload()` — read cell payload (may span overflow pages)
- **Sharc equivalent**: `IBTreeReader` with cursor-based iteration

### 2.3 Record Decoder (`vdbeaux.c`, `vdbemem.c`)
- `sqlite3VdbeRecordUnpack()` — decode record into column values
- Varint decoding in `sqlite3GetVarint()`
- **Sharc equivalent**: `IRecordDecoder` with span-based zero-copy decoding

### 2.4 Overflow Pages
When a cell payload exceeds the page's usable space:
- First portion stored inline
- Remainder in a linked list of overflow pages
- Each overflow page: 4-byte next-page pointer, then data

## 3. Minimum Viable Subset for Sharc v0.1

### Must Implement
1. **File header parsing** — validate magic, read page size, encoding, page count
2. **Page I/O** — read pages from file or `ReadOnlyMemory<byte>`
3. **Varint decoding** — high-performance span-based
4. **B-tree traversal** — table b-trees (0x05 interior, 0x0D leaf)
5. **Cell parsing** — extract rowid + payload from table leaf cells
6. **Record decoding** — serial type interpretation, column extraction
7. **Schema reading** — parse `sqlite_schema` from page 1
8. **Overflow page following** — for large records

### Deferred (v0.2+)
- Index b-tree reading
- WAL mode support
- SQL parsing / query execution
- Write operations
- FTS / R-Tree virtual tables
- Collation sequences beyond BINARY

## 4. Encoding Considerations

Sharc v0.1 targets UTF-8 databases (encoding value 1, the most common).
UTF-16 support is deferred but the architecture accommodates it via `IRecordDecoder`.

## 5. Locking Model

For read-only access, SQLite uses SHARED locks. Since Sharc is read-only:
- File-backed: open with `FileShare.ReadWrite` to coexist with writers
- Memory-backed: no locking needed
- WAL mode: **Supported** via `WalPageSource`. Merges WAL frames with main db pages in real-time.

## 6. The Performance Pivot — Surpassing the VDBE

Historically, SQLite's VDBE (Virtual Database Engine) was the gold standard for filter performance. By evaluating predicates inside the C-based scan loop, it avoided the overhead of crossing the P/Invoke or JS/WASM boundary for every row.

Sharc v1.0 has surpassed this limitation through the **Filter Star (JIT)** pipeline:

### 6.1 Offset Hoisting
SQLite's VDB often re-parses record headers to locate column offsets for multiple predicates. Sharc's `FilterNode` performs a single, hoisted pass over the record header, populating a stack-allocated offset buffer. Predicates then jump directly to the byte payload via `ReadOnlySpan<byte>`.

### 6.2 De-virtualization
The entire filter tree is JIT-compiled into a flattened, branch-efficient C# Lambda. This eliminates the virtual dispatch and bytecode interpretation overhead that costs SQLite ~150-200ns per row.

### 6.3 Result: 25% Lead
In current benchmarks (5k rows, 2 predicates), **Sharc (496μs) vs SQLite (659μs)** demonstrates that a zero-alloc managed pipeline, specialized at runtime by the .NET JIT, is now faster than the generic native bytecode loop.
