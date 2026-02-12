# Sharc vs Microsoft.Data.Sqlite — Benchmark Specification

## Principles

These benchmarks are designed to be **defensible under scrutiny**. Every benchmark must:

1. **Test the same logical operation** on both sides
2. **Use the same dataset** — identical .db file, same schema, same data
3. **Give SQLite every advantage** — pre-opened connections, pre-prepared statements, 
   connection pooling enabled, WAL mode where it helps
4. **Show where Sharc loses** — credibility comes from honesty, not cherry-picking
5. **Use realistic data** — not just integers, not just 5 rows
6. **Measure what matters** — time, heap allocation, and GC pressure

### Test Database Specification

Create a canonical test database with these tables:

```sql
-- Small lookup table (100 rows)
CREATE TABLE config (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Medium table (10,000 rows) — mixed types, realistic shape
CREATE TABLE users (
    id         INTEGER PRIMARY KEY,
    username   TEXT NOT NULL,          -- 8-20 chars
    email      TEXT NOT NULL,          -- 15-40 chars
    bio        TEXT,                   -- NULL 30% of rows, 50-500 chars when present
    age        INTEGER NOT NULL,       -- 18-90
    balance    REAL NOT NULL,          -- 0.00 - 99999.99
    avatar     BLOB,                   -- NULL 50% of rows, 1-4 KB when present
    is_active  INTEGER NOT NULL,       -- 0 or 1
    created_at TEXT NOT NULL           -- ISO 8601 datetime string
);

-- Large table (100,000 rows) — narrow, integer-heavy (IoT/telemetry shape)
CREATE TABLE events (
    id         INTEGER PRIMARY KEY,
    user_id    INTEGER NOT NULL,
    event_type INTEGER NOT NULL,       -- 1-50
    timestamp  INTEGER NOT NULL,       -- unix epoch
    value      REAL,                   -- NULL 20% of rows
    FOREIGN KEY (user_id) REFERENCES users(id)
);

-- Wide table (1,000 rows) — many columns (analytics/reporting shape)
CREATE TABLE reports (
    id      INTEGER PRIMARY KEY,
    label   TEXT NOT NULL,             -- 10-30 chars
    col_01  REAL, col_02  REAL, col_03  REAL, col_04  REAL, col_05  REAL,
    col_06  REAL, col_07  REAL, col_08  REAL, col_09  REAL, col_10  REAL,
    col_11  REAL, col_12  REAL, col_13  REAL, col_14  REAL, col_15  REAL,
    col_16  REAL, col_17  REAL, col_18  REAL, col_19  REAL, col_20  REAL,
    notes   TEXT                       -- NULL 60% of rows, 100-1000 chars when present
);

-- Indexes for fair indexed lookups
CREATE INDEX idx_users_username ON users(username);
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_events_user_id ON events(user_id);
CREATE INDEX idx_events_timestamp ON events(timestamp);
CREATE INDEX idx_events_user_time ON events(user_id, timestamp);
```

### Measurement Protocol

For every benchmark:
- Report **median** time (not mean — avoids outlier skew)
- Report **heap bytes allocated** (BenchmarkDotNet `[MemoryDiagnoser]`)
- Report **Gen0/Gen1/Gen2 collections** where non-zero
- Run minimum 100 iterations after warmup
- Pin to single core if measuring single-threaded to avoid migration noise

---

## Category 1: Database Open & Setup

**Why it matters:** Opening a database is the first thing every consumer does.
Sharc's in-memory mode should dominate; file-based modes may lose. Show both.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 1.1 | Open from pre-loaded byte[] | `SharcDatabase.OpenMemory(bytes)` | `new SqliteConnection("Data Source=:memory:")` + bulk load |
| 1.2 | Open from file (cold) | `SharcDatabase.Open("test.db")` | `new SqliteConnection("Data Source=test.db")` + `.Open()` |
| 1.3 | Open from file (warm, OS-cached) | Same as 1.2, second run | Same, connection pooling enabled |
| 1.4 | Open memory-mapped | `SharcDatabase.OpenMmap("test.db")` | N/A — note SQLite uses mmap internally but no managed API |
| 1.5 | Open + read 1 row + close | Full round trip | Full round trip |

**Expected outcome:** Sharc wins in-memory by 20x+. SQLite wins or ties on file open
(pooling amortises native setup). Benchmark 1.5 is the real-world one — show the full cost.

---

## Category 2: Schema & Metadata Introspection

**Why it matters:** Tools, ORMs, and migration systems read schema constantly.
This is where Sharc's direct struct parsing vs SQLite's PRAGMA-through-SQL overhead
should be most dramatic.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 2.1 | Read page size | Parse header byte 16-17 | `PRAGMA page_size` |
| 2.2 | Read all header fields | Parse 100-byte header | `PRAGMA page_size` + `page_count` + `encoding` + `schema_version` + `user_version` |
| 2.3 | List all table names | Walk sqlite_master B-tree | `SELECT name FROM sqlite_master WHERE type='table'` |
| 2.4 | Get column info for 1 table | Parse table schema from sqlite_master | `PRAGMA table_info('users')` |
| 2.5 | Get column info for all tables | Parse all table schemas | `PRAGMA table_info(...)` for each table |
| 2.6 | Batch 100 schema reads | Repeat 2.3 × 100 | Repeat 2.3 × 100 |

**Expected outcome:** Sharc wins all of these, probably by 100x–10,000x.
This is Sharc's strongest category.

---

## Category 3: Point Lookups (Single Row by Primary Key)

**Why it matters:** This is the most common database operation in applications.
B-tree traversal efficiency is directly tested. This is where the comparison
gets real — both sides must walk the B-tree.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 3.1 | Lookup 1 row by rowid (int PK) | B-tree seek to rowid, decode row | `SELECT * FROM users WHERE id = @id` (prepared) |
| 3.2 | Lookup 1 row, read 1 column | B-tree seek, decode only target column | `SELECT username FROM users WHERE id = @id` |
| 3.3 | Lookup 1 row by text index | Index B-tree seek → table B-tree seek | `SELECT * FROM users WHERE username = @name` |
| 3.4 | Lookup 10 random rows by rowid | 10 independent seeks | 10 executions of prepared statement |
| 3.5 | Lookup 100 random rows by rowid | 100 independent seeks | 100 executions of prepared statement |
| 3.6 | Lookup row that doesn't exist | B-tree seek, miss | `SELECT * FROM users WHERE id = 99999999` |
| 3.7 | Check existence only (no decode) | B-tree seek, return bool | `SELECT 1 FROM users WHERE id = @id` → HasRows |

**Expected outcome:** This is the critical category. Sharc should win because:
(a) no interop boundary, (b) no SQL parsing (even prepared statements have dispatch
overhead), (c) zero-alloc row decoding. But the margin may be smaller than metadata
benchmarks because both sides do real B-tree traversal work. Honest margins here
(even 5-20x) are more convincing than inflated metadata numbers.

**Notes on fairness:**
- SQLite must use prepared statements (not re-parsed each call)
- Rowid values must be pre-selected randomly, same set for both sides
- Warm the B-tree pages into OS cache before timing

---

## Category 4: Sequential Scan (Full Table)

**Why it matters:** ETL, exports, analytics, backup — any "read everything" workload.
Tests throughput rather than latency.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 4.1 | Scan 100 rows, integers only | Scan `config` or subset | `SELECT key, value FROM config` |
| 4.2 | Scan 10K rows, mixed types | Scan `users`, decode all columns | `SELECT * FROM users` → read all columns |
| 4.3 | Scan 10K rows, 2 columns only | Scan `users`, decode id + username only | `SELECT id, username FROM users` |
| 4.4 | Scan 100K rows, narrow table | Scan `events`, decode all columns | `SELECT * FROM events` |
| 4.5 | Scan 100K rows, integers only | Scan `events`, decode id + event_type only | `SELECT id, event_type FROM events` |
| 4.6 | Scan 1K rows, wide table (22 cols) | Scan `reports`, decode all columns | `SELECT * FROM reports` |
| 4.7 | Scan with NULLs (count non-null bios) | Scan `users`, check bio != null | `SELECT COUNT(*) FROM users WHERE bio IS NOT NULL` |
| 4.8 | Throughput: rows per second | Scan `events` in tight loop, count | Same |

**Expected outcome:** Sharc should win by 100x–1000x+ on in-memory buffers due to
zero interop + zero allocation. The gap may narrow on string-heavy columns
(both sides must copy string bytes somewhere). Track per-row and per-byte throughput.

**Measurement additions for this category:**
- Report rows/second and MB/second
- Report total GC collections across full scan

---

## Category 5: Range Scans & Filtered Reads

**Why it matters:** Real applications don't always read everything. Range queries
over indexed columns test B-tree range traversal efficiency.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 5.1 | Range scan by rowid (100 rows) | B-tree range: id 5000–5099 | `SELECT * FROM users WHERE id BETWEEN 5000 AND 5099` |
| 5.2 | Range scan by rowid (1000 rows) | B-tree range: id 5000–5999 | `SELECT * FROM users WHERE id BETWEEN 5000 AND 5999` |
| 5.3 | Index range scan (events by user) | Index seek + table lookups | `SELECT * FROM events WHERE user_id = @uid` |
| 5.4 | Index range scan (events by time range) | Index range traversal | `SELECT * FROM events WHERE timestamp BETWEEN @t1 AND @t2` |
| 5.5 | Composite index scan | Composite index traversal | `SELECT * FROM events WHERE user_id = @uid AND timestamp BETWEEN @t1 AND @t2` |
| 5.6 | Count rows in range (no decode) | B-tree range, count cells only | `SELECT COUNT(*) FROM events WHERE user_id = @uid` |

**Expected outcome:** Sharc should win on the B-tree traversal and row decoding.
The margin depends on how much of the time is I/O vs decode. Index lookups
that require a second B-tree hop (index → table) are the truest test.

**Notes:**
- For Sharc, "range scan" means B-tree cursor from start key to end key.
  If Sharc doesn't have a cursor/range API yet, this category defines what to build.
- Use the same key ranges for both sides, pre-computed.

---

## Category 6: Data Type Decoding

**Why it matters:** SQLite's type system is dynamic (manifest typing). Decoding
efficiency varies wildly by type. This isolates the decode cost from the seek cost.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 6.1 | Decode 10K integers | Read `events.event_type` × 10K | `reader.GetInt32(2)` × 10K |
| 6.2 | Decode 10K 64-bit integers | Read `events.timestamp` × 10K | `reader.GetInt64(3)` × 10K |
| 6.3 | Decode 10K doubles | Read `events.value` × 10K | `reader.GetDouble(4)` × 10K |
| 6.4 | Decode 10K short strings (8-20 chars) | Read `users.username` × 10K | `reader.GetString(1)` × 10K |
| 6.5 | Decode 10K medium strings (50-500 chars) | Read `users.bio` × non-null rows | `reader.GetString(3)` × non-null |
| 6.6 | Decode 1K BLOBs (1-4 KB) | Read `users.avatar` × non-null rows | `reader.GetStream(6)` or `GetBytes()` × non-null |
| 6.7 | Decode NULLs (check + skip) | Read `users.bio`, check IsNull | `reader.IsDBNull(3)` × 10K |
| 6.8 | Decode mixed row (all types) | Read full `users` row, all 9 cols | `reader.Get*()` for all 9 columns |
| 6.9 | Varint decode (isolated) | Decode 100K varints from byte[] | N/A (internal to SQLite) |

**Expected outcome:** Integers and doubles — Sharc wins massively (span slice vs
interop + boxing). Strings — Sharc wins but margin depends on whether Sharc returns
Span<byte> (zero-copy) or allocates a string. BLOBs — similar. NULL checks — Sharc
should win by checking serial type byte vs SQLite's IsDBNull() round-trip.

**This is where the interface lock-in argument becomes concrete:**
- Sharc returns `ReadOnlySpan<byte>` for strings → 0 B allocated
- SQLite returns `string` → heap allocation every time
- Show both the time AND the allocation side by side per type

---

## Category 7: Multi-Page & B-Tree Stress

**Why it matters:** Databases with more data have deeper B-trees and more overflow
pages. This tests that Sharc handles real page traversal, not just single-page parsing.

| # | Benchmark | Sharc Operation | SQLite Operation |
|---|-----------|-----------------|------------------|
| 7.1 | Walk entire B-tree (leaf pages only) | Traverse all leaf pages of `events` | `SELECT id FROM events` (forces full tree walk) |
| 7.2 | Walk B-tree counting pages | Count interior + leaf pages | N/A (Sharc-only, demonstrates tree shape) |
| 7.3 | Overflow page read (large row) | Read row with large bio/blob that spans pages | `SELECT bio FROM users WHERE id = @id` (pick long bio) |
| 7.4 | Random page access pattern | Read 100 random pages by number | N/A (Sharc-only, tests page cache / mmap) |
| 7.5 | Deep B-tree traversal | Point lookup in 100K-row table (4+ levels) | `SELECT * FROM events WHERE id = @id` |

**Expected outcome:** These test Sharc's page I/O layer under realistic conditions.
Performance should be strong for in-memory and mmap modes. File stream mode may
show higher variance.

---

## Category 8: Realistic Application Workloads

**Why it matters:** Individual micro-benchmarks are useful but can be misleading.
These composite benchmarks simulate what real applications actually do.

| # | Benchmark | Description | Operations |
|---|-----------|-------------|------------|
| 8.1 | "Load user profile" | Single user lookup + decode all fields | Seek by rowid → decode 9 columns including nullable blob |
| 8.2 | "User activity feed" | Lookup user, then their recent events | Seek user by id → range scan events by user_id, last 50 |
| 8.3 | "Search user by email" | Index lookup on text column | Index B-tree seek → table B-tree seek → decode row |
| 8.4 | "Export users to CSV" | Full table scan with string formatting | Scan 10K users → convert each column to string representation |
| 8.5 | "Dashboard count" | Count rows matching criteria | Count events for user_id = X where timestamp > T |
| 8.6 | "Schema migration check" | Read all tables, all columns, all indexes | Full sqlite_master parse + column info for each table |
| 8.7 | "Batch lookup" | 500 random user lookups | 500 independent rowid seeks + full row decode |
| 8.8 | "Config read" | Read 10 config values by key | 10 text PK lookups on small table |

**Expected outcome:** These are the benchmarks that tell the real story. A 20x win
on "load user profile" is worth more than a 7,000x win on "read page size" because
it represents actual application value. Lead with these in the article.

---

## Category 9: Memory & GC Pressure Under Load

**Why it matters:** Allocation numbers per-operation are useful, but sustained load
reveals GC impact. A library that allocates 0 B per read but runs 100K reads
means zero GC pauses. A library that allocates 984 B per read across 100K reads
triggers multiple Gen0/Gen1 collections.

| # | Benchmark | Scenario | Measure |
|---|-----------|----------|---------|
| 9.1 | Sustained scan GC impact | Scan `events` (100K rows) × 10 iterations | Total Gen0/Gen1/Gen2 collections |
| 9.2 | Sustained lookup GC impact | 100K random lookups on `users` | Total Gen0/Gen1/Gen2 collections |
| 9.3 | Peak working set | Full scan of largest table | Peak managed heap size |
| 9.4 | Allocation rate | Full scan of `events` | Bytes allocated per second |

**Expected outcome:** This is where zero-allocation design pays off dramatically.
SQLite may trigger dozens of Gen0 collections where Sharc triggers zero.
In latency-sensitive applications (game servers, trading systems, real-time
pipelines), this is the argument that sells.

---

## Category 10: Concurrency (If Applicable)

**Why it matters:** Read-heavy workloads often use multiple threads. Tests thread
safety overhead and contention patterns.

| # | Benchmark | Scenario |
|---|-----------|----------|
| 10.1 | 4 threads, independent scans | Each thread scans a different table |
| 10.2 | 4 threads, same table | All threads scan `events` simultaneously |
| 10.3 | 4 threads, random lookups | All threads do random rowid lookups on `users` |

**Expected outcome:** Sharc with in-memory ReadOnlyMemory<byte> should scale
near-linearly (no locking needed on immutable data). SQLite's connection-per-thread
model adds overhead. Memory-mapped Sharc should also scale well.

---

## Presentation Order for the Article

Don't lead with metadata benchmarks. They're impressive numbers but feel synthetic.

**Recommended order:**

1. **Realistic workloads (Cat 8)** — lead with "load user profile" and "batch lookup."
   These are operations your reader can relate to. Even a 10-20x win here is compelling.

2. **Point lookups (Cat 3)** — the bread and butter. Show single-row and batch lookups.
   This is where the interop boundary argument becomes measurable.

3. **Sequential scans (Cat 4)** — throughput numbers. Report as rows/sec and MB/sec.
   The allocation column tells the GC story.

4. **Data type decoding (Cat 6)** — this is where the locked-in interface argument
   gets concrete evidence. Show Span<byte> vs string allocation per type.

5. **GC pressure (Cat 9)** — the "so what?" for the allocation numbers. Show that
   100K reads = 0 GC collections for Sharc vs N collections for SQLite.

6. **Range scans (Cat 5)** — demonstrates B-tree maturity, not just point access.

7. **Schema/metadata (Cat 2)** — now it's supporting evidence, not the headline.

8. **Database open (Cat 1)** — context, not the main story.

**For each benchmark card, show:**
- Operation name (plain English, not method names)
- Sharc execution time
- SQLite execution time
- Speed ratio
- Sharc heap allocated
- SQLite heap allocated
- Allocation ratio

---

## Fairness Checklist

Before publishing any benchmark, verify:

- [ ] Same .db file used for both sides (byte-identical)
- [ ] SQLite connection opened and statement prepared BEFORE timing starts
- [ ] SQLite connection pooling is ON (default behavior)
- [ ] Sharc byte[] loaded into memory BEFORE timing starts (for memory benchmarks)
- [ ] For file-based benchmarks, OS page cache is warm (run once before measuring)
- [ ] Random rowids / keys are pre-generated, same set for both sides
- [ ] BenchmarkDotNet with `[MemoryDiagnoser]` and `[GcDiagnoser]`
- [ ] Minimum 100 iterations after warmup
- [ ] Results include benchmarks where Sharc LOSES (file open, mmap setup)
- [ ] No `GC.Collect()` between iterations (let BenchmarkDotNet manage)
- [ ] Report .NET version, OS, CPU, and RAM in every card footer

---

## Benchmarks Where SQLite Will (Probably) Win

Include these. Credibility comes from honesty.

| Scenario | Why SQLite Wins |
|----------|-----------------|
| File open (cold, first access) | SQLite's native open is fast; Sharc must read/map the whole file |
| Memory-mapped setup | OS mmap syscall has fixed overhead |
| Very large BLOBs (>1 MB) | SQLite's incremental blob I/O may outperform full-page reads |
| Complex queries (when Sharc adds SQL) | SQLite's query planner is decades-mature |
| Write operations (future) | Native write + WAL journal is highly optimised |
| Connection pooling amortisation | Repeated open/close favors SQLite's pool |

Showing these builds trust. The reader thinks: "they tested honestly and Sharc
still wins where it matters."
