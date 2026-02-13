# Write Engine Action Plan

**Source:** `secrets/SharcWriteEngine.md`
**Status:** TODO
**Priority:** P2 (depends on Filter Engine Phase 1 for `DeleteWhere` in Phase 3)
**Estimated Effort:** 8 weeks (3 phases)

---

## Current State Inventory — Write-Path Primitives

| Primitive | File | Exists? | Write-Ready? |
|:---|:---|:---:|:---|
| `VarintDecoder.Write(Span, long)` | `src/Sharc.Core/Primitives/VarintDecoder.cs` | **YES** | Returns int (bytes written), AggressiveInlining |
| `VarintDecoder.GetEncodedLength(long)` | same | **YES** | Returns int (1-9) |
| `SerialTypeCodec.GetContentSize(long)` | `src/Sharc.Core/Primitives/SerialTypeCodec.cs` | **YES** | Read-side — reused for offset calc |
| `SerialTypeCodec.GetSerialType(ColumnValue)` | same | **NO** | Inverse of GetContentSize — NEW |
| `CellParser.CalculateInlinePayloadSize(int, int)` | `src/Sharc.Core/BTree/CellParser.cs` | **YES** | Shared read/write |
| `CellParser.ParseTableLeafCell(...)` | same | **YES** | Read-side only |
| `WalReader.ComputeChecksum(...)` | `src/Sharc.Core/IO/WalReader.cs` | **YES** | Private static — needs refactor to internal |
| `DatabaseHeader.Parse(ReadOnlySpan)` | `src/Sharc.Core/Format/DatabaseHeader.cs` | **YES** | Read-only struct — needs Write() |
| `BTreePageHeader.Parse(ReadOnlySpan)` | `src/Sharc.Core/Format/BTreePageHeader.cs` | **YES** | Read-only struct — needs Write() |
| `WalHeader.Parse(ReadOnlySpan)` | `src/Sharc.Core/Format/WalHeader.cs` | **YES** | Read-only struct — needs Write() |
| `WalFrameHeader.Parse(ReadOnlySpan)` | `src/Sharc.Core/Format/WalFrameHeader.cs` | **YES** | Read-only struct — needs Write() |
| `IPageSource` | `src/Sharc.Core/IPageSource.cs` | **YES** | Read-only — extend to IPageStore |
| `RecordDecoder` | `src/Sharc.Core/Records/RecordDecoder.cs` | **YES** | Read-only — inverse = RecordEncoder |

**Summary: 6 of 15 primitives already exist. 9 new primitives needed (exact mirrors of existing read code).**

---

## Phase 1 — Core Write Path (4 weeks, ~150 tests)

**Goal:** End-to-end writes producing SQLite-compatible database files. INSERT, UPDATE, UPSERT, Transactions.

### Week 1 — Record Encoder + Cell Builder

#### Task 1.1: `SerialTypeCodec.GetSerialType(ColumnValue)` — value → serial type
- **File:** `src/Sharc.Core/Primitives/SerialTypeCodec.cs` (MODIFY)
- **What:** New static method. Given a `ColumnValue`, return the optimal SQLite serial type:
  - NULL → 0
  - Int 0 → 8 (constant 0, zero bytes)
  - Int 1 → 9 (constant 1, zero bytes)
  - Int fits in 1 byte → 1, 2 bytes → 2, 3 bytes → 3, 4 bytes → 4, 6 bytes → 5, 8 bytes → 6
  - Double → 7
  - Text of N bytes → 2*N + 13 (odd serial types ≥ 13)
  - Blob of N bytes → 2*N + 12 (even serial types ≥ 12)
- **Tests:** 15 tests in `tests/Sharc.Tests/Write/SerialTypeCodecWriteTests.cs`
  - NULL → 0
  - Int 0 → 8, Int 1 → 9
  - Small ints: -128 → 1, 32767 → 2, etc.
  - Boundary values: Int24 max, Int32 max, Int48 max, Int64 max
  - Negative boundary values
  - Double → 7
  - Text: empty (→ 13), 1 byte (→ 15), 100 bytes (→ 213)
  - Blob: empty (→ 12), 1 byte (→ 14)
  - Round-trip: `GetSerialType(value)` → `GetContentSize(serialType)` matches value byte length
- **Depends on:** Nothing

#### Task 1.2: `RecordEncoder.EncodeRecord()` — values → SQLite record bytes
- **File:** `src/Sharc.Core/Records/RecordEncoder.cs` (NEW)
- **What:** `internal static class RecordEncoder` with:
  - `EncodeRecord(ReadOnlySpan<ColumnValue> columns, Span<byte> destination) → int` — writes header (header-size varint + serial type varints) + body (values in order). Returns total bytes written.
  - `ComputeEncodedSize(ReadOnlySpan<ColumnValue> columns) → int` — pre-compute total size without writing.
  - Record format:
    1. Header-size varint (includes itself)
    2. Serial type varints for each column
    3. Body: column values in serial type order
  - Integer encoding: use `BinaryPrimitives.Write*BigEndian()` for types 1-6
  - Text/Blob: direct span copy
  - NULL/constant (types 0, 8, 9): zero body bytes
- **Tests:** 20 tests in `tests/Sharc.Tests/Write/RecordEncoderTests.cs`
  - Single column: NULL, int, double, text, blob
  - Multi-column: mixed types (int + text + NULL + double)
  - Round-trip: `EncodeRecord()` → `RecordDecoder.DecodeRecord()` → identical ColumnValue[]
  - Edge cases: empty text, empty blob, max int64, negative int, NaN double
  - ComputeEncodedSize matches actual EncodeRecord output length
  - Large text (1KB+) — verify serial type and body correctly encoded
  - Fuzz: 10 random ColumnValue[] through encode → decode cycle
- **Depends on:** Task 1.1 (SerialTypeCodec.GetSerialType)

#### Task 1.3: `CellBuilder` — record payload → B-tree cell
- **File:** `src/Sharc.Core/BTree/CellBuilder.cs` (NEW)
- **What:** `internal static class CellBuilder` with:
  - `BuildTableLeafCell(long rowId, ReadOnlySpan<byte> recordPayload, Span<byte> destination, int usablePageSize) → int`
    - Format: payload-size varint + rowid varint + inline payload [+ 4-byte overflow page pointer if overflow]
    - Use `CellParser.CalculateInlinePayloadSize()` (already exists) to determine split point
  - `BuildTableInteriorCell(uint leftChildPage, long rowId, Span<byte> destination) → int`
    - Format: 4-byte child page (big-endian) + rowid varint
  - `ComputeTableLeafCellSize(long rowId, int recordPayloadSize, int usablePageSize) → int`
    - Pre-compute without writing
- **Tests:** 15 tests in `tests/Sharc.Tests/Write/CellBuilderTests.cs`
  - Leaf cell: small record (fits in page), rowid = 1
  - Leaf cell: rowid requiring multi-byte varint
  - Leaf cell: overflow case (record > inline payload size) — verify 4-byte overflow pointer appended
  - Interior cell: standard case
  - Interior cell: large rowid
  - Round-trip: `BuildTableLeafCell()` → `CellParser.ParseTableLeafCell()` → same rowid + payload
  - Round-trip: `BuildTableInteriorCell()` → `CellParser.ParseTableInteriorCell()` → same child page + rowid
  - ComputeTableLeafCellSize matches actual BuildTableLeafCell output
- **Depends on:** `CellParser.CalculateInlinePayloadSize()` (exists), `VarintDecoder.Write()` (exists)

**Week 1: ~50 tests, 2 new files + 1 modified**

---

### Week 2 — Page Manager + WAL Writer

#### Task 2.1: Refactor `WalReader.ComputeChecksum` to shared internal static
- **File:** `src/Sharc.Core/IO/WalReader.cs` (MODIFY)
- **What:** Change `ComputeChecksum(ReadOnlySpan<byte> data, bool bigEndian, ref uint s0, ref uint s1)` from `private static` to `internal static`. No logic change — just visibility.
- **Tests:** Existing WAL tests must still pass
- **Depends on:** Nothing

#### Task 2.2: Header Write methods
- **Files:** (all MODIFY — add Write methods to existing readonly structs)
  - `src/Sharc.Core/Format/DatabaseHeader.cs` — `static void Write(Span<byte> destination, DatabaseHeader header)` or `WriteTo(Span<byte>)` instance method. Writes all 100 bytes including magic string, page size, format versions, change counter, page count, freelist pointers, schema cookie, etc.
  - `src/Sharc.Core/Format/BTreePageHeader.cs` — `static void Write(Span<byte> destination, BTreePageHeader header)`. Writes 8 bytes (leaf) or 12 bytes (interior) including page type, freeblock offset, cell count, cell content offset, fragmented bytes, right child (interior only).
  - `src/Sharc.Core/Format/WalHeader.cs` — `static void Write(Span<byte> destination, WalHeader header)`. Writes 32-byte WAL header: magic, format version, page size, checkpoint seq, salt1, salt2, checksum1, checksum2.
  - `src/Sharc.Core/Format/WalFrameHeader.cs` — `static void Write(Span<byte> destination, WalFrameHeader header)`. Writes 24-byte frame header: page number, db size, salt1, salt2, checksum1, checksum2.
- **Tests:** 12 tests in `tests/Sharc.Tests/Write/HeaderWriteTests.cs`
  - DatabaseHeader: Write → Parse round-trip (all fields preserved)
  - BTreePageHeader: leaf Write → Parse, interior Write → Parse
  - WalHeader: Write → Parse round-trip
  - WalFrameHeader: Write → Parse (commit frame), Write → Parse (non-commit frame)
  - DatabaseHeader: verify magic string bytes at offset 0
  - BTreePageHeader: verify page type byte values (0x0D leaf table, 0x05 interior table)
- **Depends on:** Nothing

#### Task 2.3: `IPageStore` interface
- **File:** `src/Sharc.Core/IPageStore.cs` (NEW)
- **What:** Extends `IPageSource` with write operations:
  ```csharp
  public interface IPageStore : IPageSource
  {
      void WritePage(uint pageNumber, ReadOnlySpan<byte> data);
      uint AllocatePage();
      void FreePage(uint pageNumber);
      void Sync();
  }
  ```
- **Tests:** None (interface only)
- **Depends on:** `IPageSource` (exists)

#### Task 2.4: `PageManager` — dirty page buffer + allocation
- **File:** `src/Sharc.Core/IO/PageManager.cs` (NEW)
- **What:** `internal sealed class PageManager : IDisposable`
  - Wraps `IPageSource` (read-only main DB) + Dictionary<uint, byte[]> dirty pages
  - `GetPageForWrite(uint pageNumber) → Span<byte>`: COW — reads from main DB into `ArrayPool<byte>.Shared.Rent()` buffer, stores in dirty map
  - `AllocatePage() → uint`: pop from freelist (read `DatabaseHeader.FirstFreelistPage`, parse trunk pages), or extend file (increment page count)
  - `FreePage(uint pageNumber)`: push to freelist
  - `GetDirtyPages() → IEnumerable<(uint, ReadOnlyMemory<byte>)>`: return all modified pages
  - `Reset()`: return all pooled buffers, clear dirty map (after commit)
- **Tests:** 10 tests in `tests/Sharc.Tests/Write/PageManagerTests.cs`
  - GetPageForWrite: first access COWs from source, second access returns same buffer
  - AllocatePage: returns next page number when freelist empty
  - AllocatePage: pops from freelist when available
  - FreePage: page added to freelist
  - GetDirtyPages: returns all modified pages
  - Reset: clears dirty map, returns buffers to pool
  - Multiple pages: track 5 dirty pages independently
- **Depends on:** Task 2.3 (IPageStore)

#### Task 2.5: `WalWriter` — append WAL frames
- **File:** `src/Sharc.Core/IO/WalWriter.cs` (NEW)
- **What:** `internal sealed class WalWriter : IDisposable`
  - Constructor: `WalWriter(string walPath, int pageSize, DatabaseHeader dbHeader)`
  - `WriteHeader()`: write 32-byte WAL header (magic, format version, page size, salt, initial checksums)
  - `AppendFrame(uint pageNumber, ReadOnlySpan<byte> pageData)`: write 24-byte frame header + page data. Non-commit frame (DbSizeAfterCommit = 0). Cumulative checksum via `WalReader.ComputeChecksum()`.
  - `AppendCommitFrame(uint pageNumber, ReadOnlySpan<byte> pageData, uint dbSizeInPages)`: same but with non-zero DbSizeAfterCommit (marks transaction commit boundary)
  - `Sync()`: flush + fsync WAL file
  - Uses cumulative checksum state (s0, s1) across frames
- **Tests:** 13 tests in `tests/Sharc.Tests/Write/WalWriterTests.cs`
  - WriteHeader: produces 32 bytes, parseable by WalHeader.Parse
  - AppendFrame: single frame, read back with WalReader.ReadFrameMap → page present
  - AppendFrame: 3 frames, all appear in frame map
  - AppendCommitFrame: DbSizeAfterCommit non-zero, IsCommitFrame == true
  - Cumulative checksums: write 5 frames, read back, all checksums valid
  - Sync: file flushed (verifiable by re-reading from disk)
  - Round-trip: write WAL → read with existing WalReader → identical page data
  - New WAL file created correctly
  - Append to existing WAL file
- **Depends on:** Task 2.1 (shared ComputeChecksum), Task 2.2 (WalHeader.Write, WalFrameHeader.Write)

**Week 2: ~35 tests, 4 new files + 5 modified**

---

### Week 3 — B-Tree Mutator (Insert + Split)

#### Task 3.1: `BTreeMutator` — insert into B-tree
- **File:** `src/Sharc.Core/BTree/BTreeMutator.cs` (NEW)
- **What:** `internal sealed class BTreeMutator`
  - Constructor: takes `PageManager`, `int usablePageSize`
  - `Insert(uint rootPage, long rowId, ReadOnlySpan<byte> recordPayload)`:
    1. Walk B-tree from root to correct leaf (same traversal as BTreeCursor, but tracks path)
    2. Find insertion point in leaf (cells sorted by rowid)
    3. If cell fits: shift cell pointers, write cell to free space area, update page header
    4. If page full: trigger page split
  - `GetMaxRowId(uint rootPage) → long`: scan rightmost leaf for auto-increment
  - Internal: `FindLeaf(uint rootPage, long rowId) → (uint leafPage, int insertIndex, Stack<uint> path)` — B-tree descent tracking ancestors for splits
  - Internal: `InsertCellIntoPage(uint pageNumber, int insertIndex, ReadOnlySpan<byte> cellData)` — the low-level page operation

#### Task 3.2: Page split logic
- **In:** `src/Sharc.Core/BTree/BTreeMutator.cs` (same file as 3.1)
- **What:**
  - `SplitLeafPage(uint fullPage, int insertIndex, ReadOnlySpan<byte> newCell, Stack<uint> ancestors)`:
    1. Allocate new page via PageManager
    2. Divide cells by total byte size (not count) — roughly 50/50 split
    3. Move upper half to new page
    4. Determine median key for promotion
    5. Insert median key + new page pointer into parent interior page
    6. If parent overflows, split recursively up the ancestor stack
    7. If root splits, allocate new root (tree grows taller by 1)
  - `SplitInteriorPage(uint fullPage, ...)` — similar but for interior nodes
  - Cell pointer array management: read existing pointers, insert new one in sorted position, write back
  - Free space management: track cell content area, grow downward from page end

#### Task 3.3: B-Tree Mutator tests
- **File:** `tests/Sharc.Tests/Write/BTreeMutatorTests.cs` (NEW)
- **Tests:** 30 tests
  - Insert 1 record into empty B-tree → readable by BTreeCursor
  - Insert 10 records → all readable in order
  - Insert 100 records → causes page splits, all readable
  - Insert 1000 records → multiple levels of splits, all readable
  - Insert records in reverse order → B-tree still valid
  - Insert records in random order → B-tree still valid
  - GetMaxRowId: empty table → 0, populated → correct max
  - Page split produces valid B-tree invariants:
    - All cells in sorted rowid order within each page
    - All leaf pages at same depth (depth-balanced)
    - Parent interior keys correctly bound child page ranges
    - Cell count matches actual cells on page
    - Cell content offset is valid
  - Interior page split (deep tree, > 2 levels)
  - Root split (tree grows taller)
  - Records with overflow (large payload) — verify overflow chain navigable
  - Insert at beginning, middle, end of page
  - Page fill factor: after split, both pages roughly half full
- **Depends on:** Tasks 1.2, 1.3, 2.4 (RecordEncoder, CellBuilder, PageManager)

**Week 3: ~30 tests, 1 new file (but complex)**

---

### Week 4 — Transaction Manager + Public API

#### Task 4.1: `FileLock` — SQLite-compatible file locking
- **File:** `src/Sharc/Write/FileLock.cs` (NEW)
- **What:** `internal sealed class FileLock : IDisposable`
  - SQLite lock byte ranges: SHARED [120..120], RESERVED [121..121], EXCLUSIVE [120..127]
  - `AcquireReserved()` → acquires RESERVED lock via `FileStream.Lock(121, 1)`
  - `AcquireExclusive()` → acquires EXCLUSIVE lock via `FileStream.Lock(120, 8)`
  - `ReleaseReserved()`, `ReleaseExclusive()` → corresponding unlock
  - `bool TryAcquireReserved()` → non-blocking attempt
  - Uses pending byte (offset 0x40000000) per SQLite protocol
- **Tests:** 5 tests in `tests/Sharc.Tests/Write/FileLockTests.cs`
  - Acquire and release RESERVED
  - Acquire and release EXCLUSIVE
  - Two writers: second AcquireReserved fails (lock contention)
  - Lock is released on Dispose
  - TryAcquireReserved returns false when held by another

#### Task 4.2: `TransactionManager`
- **File:** `src/Sharc/Write/TransactionManager.cs` (NEW)
- **What:** `internal sealed class TransactionManager : IDisposable`
  - State: `TransactionState` enum (None, Active, Committed, RolledBack)
  - `Begin()`: acquire RESERVED lock, initialize PageManager
  - `Commit()`: write all dirty pages to WAL via WalWriter.AppendFrame, final frame via AppendCommitFrame, Sync, release lock, PageManager.Reset()
  - `Rollback()`: discard dirty pages (PageManager.Reset()), release lock
  - `AddDirtyPage(uint, byte[])` — delegate to PageManager
  - Auto-commit wrapper: `RunAutoCommit(Action<BTreeMutator> operation)` — Begin, execute, Commit (or Rollback on exception)
- **Tests:** 8 tests in `tests/Sharc.Tests/Write/TransactionManagerTests.cs`
  - Begin sets state to Active
  - Commit writes frames to WAL, sets state to Committed
  - Rollback discards changes, sets state to RolledBack
  - Double commit throws
  - Commit after rollback throws
  - Begin when already active throws
  - Auto-commit: success path commits
  - Auto-commit: exception path rolls back

#### Task 4.3: `SharcWriter` — public write API
- **File:** `src/Sharc/SharcWriter.cs` (NEW)
- **What:** `public sealed class SharcWriter : IDisposable`
  - `static SharcWriter Open(string path)` / `Open(string path, SharcOpenOptions options)`
  - Single-record operations (auto-commit):
    - `long Insert(string tableName, params ColumnValue[] values)` — returns assigned rowid
    - `void Update(string tableName, long rowId, params ColumnValue[] values)` — delete old + insert new at same rowid
    - `long Upsert(string tableName, long rowId, params ColumnValue[] values)` — try update, insert if not found
  - Batch operations:
    - `long[] InsertBatch(string tableName, IEnumerable<ColumnValue[]> records)` — single transaction
  - Explicit transactions:
    - `SharcTransaction BeginTransaction()`
  - Internal: uses SchemaReader to resolve table → root page, column definitions
  - Internal: RecordEncoder → CellBuilder → BTreeMutator → TransactionManager pipeline
- **Tests:** Covered by integration tests (Task 4.5)

#### Task 4.4: `SharcTransaction`
- **File:** `src/Sharc/SharcTransaction.cs` (NEW)
- **What:** `public sealed class SharcTransaction : IDisposable`
  - Same operations as SharcWriter but within explicit transaction scope
  - `long Insert(...)`, `void Update(...)`, `long Upsert(...)`, `void Delete(...)`
  - `Commit()`, `Rollback()`
  - Dispose auto-rolls-back if not committed
- **Tests:** Covered by integration tests (Task 4.5)

#### Task 4.5: Integration tests — Write + Read round-trips
- **File:** `tests/Sharc.IntegrationTests/WriteReadRoundTripTests.cs` (NEW)
- **Tests:** 20 tests
  - **Sharc write → Sharc read:**
    - Insert 1 row → read back → identical
    - Insert 100 rows → sequential scan → all present and correct
    - Insert with NULL values → read back → NULLs preserved
    - Insert various types (int, double, text, blob) → round-trip
    - Update existing row → read back → updated values
    - Upsert (insert path) → read back
    - Upsert (update path) → read back
    - InsertBatch 1000 rows → scan → all present
    - Explicit transaction: begin, 5 inserts, commit → all visible
    - Explicit transaction: begin, 5 inserts, rollback → none visible
  - **Sharc write → SQLite read (interop):**
    - Insert with Sharc → open with Microsoft.Data.Sqlite → SELECT * → identical data
    - Insert 100 rows with Sharc → SQLite SELECT COUNT = 100
    - Insert various types → SQLite reads correct types and values
  - **SQLite write → Sharc read (already works, verify not regressed):**
    - SQLite INSERT → Sharc scan → identical
  - **Mixed-mode:**
    - SQLite writes 50 rows → Sharc writes 50 more → both read all 100
  - **Concurrency:**
    - Sharc writer active → Sharc reader sees snapshot (pre-commit data invisible)
    - After commit → reader sees new data
  - **Error cases:**
    - Insert into non-existent table → throws
    - Update non-existent rowid → throws (or no-op per design choice)
- **Depends on:** Tasks 4.3, 4.4

#### Task 4.6: Write benchmarks
- **File:** `bench/Sharc.Benchmarks/WriteBenchmarks.cs` (NEW)
- **What:**
  - `SingleRow_AutoCommit`: 10,000 rows, each auto-commit. Gate: ≤ 12 μs/row
  - `Batch_1000Rows`: single transaction. Gate: ≤ 4 μs/row (250K/sec)
  - `Allocation_SingleInsert`: MemoryDiagnoser. Gate: ≤ 4.5 KB
  - `Sharc_vs_SQLite_Batch1000`: comparative. Informational (no gate yet in Phase 1)
- **Depends on:** Task 4.3

**Week 4: ~35 tests, 4 new files + benchmark file**

---

## Phase 1 Exit Criteria

- [ ] 150 new tests passing
- [ ] Round-trip verified: Sharc-written DB readable by Sharc AND SQLite
- [ ] Single-row auto-commit: ≤ 12 μs/row (WAL, sync=normal)
- [ ] Batch 1K rows: ≤ 4 μs/row (250,000 rows/sec)
- [ ] Allocation per single insert: ≤ 4.5 KB
- [ ] vs SQLite (same workload): ≤ 1.0x (match or beat)
- [ ] WAL mode: readers not blocked during writes
- [ ] Auto-commit + explicit transactions both working
- [ ] 3 benchmark gates passing
- [ ] All existing read-path tests still green (zero regression)
- [ ] `dotnet test` — all green

---

## Phase 2 — DELETE, RELATE, Schema DDL (2 weeks, ~70 tests)

### Week 5 — Delete + Graph Writes

#### Task 5.1: `BTreeMutator.Delete(uint rootPage, long rowId)`
- **File:** `src/Sharc.Core/BTree/BTreeMutator.cs` (MODIFY)
- **What:**
  - Navigate to leaf, find cell by rowid
  - Remove cell: add cell space to freeblock chain within the page
  - Freeblock coalescing: merge adjacent free regions
  - Update page header: decrement cell count, update first freeblock offset
  - Mark page dirty
  - Note: page merge/rebalance for underfull pages deferred to Phase 3
- **Tests:** 12 tests in `tests/Sharc.Tests/Write/BTreeMutatorDeleteTests.cs`
  - Delete single row → not found on read
  - Delete from middle of page → remaining rows intact
  - Delete first cell, last cell
  - Delete all rows from a page → page empty
  - Delete non-existent rowid → throws or no-op
  - After delete, freeblock chain valid
  - Freeblock coalescing: delete adjacent cells → single merged free block
  - Insert-delete-insert cycle → page space reused
- **Depends on:** Phase 1 complete

#### Task 5.2: `SharcWriter.Delete(string tableName, long rowId)`
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:** Public API for single-record delete. Auto-commit.
- **Tests:** 5 integration tests in `tests/Sharc.IntegrationTests/WriteDeleteTests.cs`
  - Delete → row gone
  - Delete → SQLite confirms row gone
  - Delete in transaction → visible after commit
  - Delete in transaction → invisible after rollback
  - Delete + Insert same rowid → new data visible

#### Task 5.3: `SharcWriter.Relate()` — graph edge creation
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:**
  - `void Relate(string sourceKey, string targetKey, RelationKind kind, double weight = 1.0, string? data = null)`
  - Inserts into `_concepts` table (if source/target nodes don't exist) and `_relations` table
  - Uses existing graph schema: source_key, target_key, kind, weight, data columns
  - Must maintain same format that `ConceptStore` / `RelationStore` reads
- **Tests:** 8 tests in `tests/Sharc.IntegrationTests/GraphWriteTests.cs`
  - Relate two nodes → edge readable by SharcContextGraph
  - Relate → GetEdges returns new edge
  - Relate with weight and data → round-trip preserved
  - Multiple edges from same source → all readable
  - Relate → SQLite sees correct _relations rows
  - Relate creates concept entries if not existing
  - Relate with existing concepts → no duplicates

#### Task 5.4: `SharcWriter.CreateTable()` — schema creation
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:**
  - `void CreateTable(string tableName, params ColumnDefinition[] columns)`
  - Inserts into `sqlite_master` table (root page 1)
  - Allocates root page for new table
  - Builds CREATE TABLE SQL string for `sqlite_master.sql` column
  - Updates `DatabaseHeader.SchemaCookie` (increment by 1)
  - `ColumnDefinition` type: `public record ColumnDefinition(string Name, string TypeAffinity, bool NotNull = false, string? DefaultValue = null)`
- **File:** `src/Sharc/ColumnDefinition.cs` (NEW)
- **Tests:** 8 tests in `tests/Sharc.IntegrationTests/SchemaWriteTests.cs`
  - CreateTable → table visible to Sharc schema reader
  - CreateTable → table visible to SQLite (`SELECT * FROM sqlite_master`)
  - CreateTable with typed columns → correct column affinities
  - CreateTable → can insert rows into new table
  - CreateTable duplicate name → throws
  - CreateTable with NOT NULL constraint → column metadata correct
  - SchemaCookie incremented after CreateTable

#### Task 5.5: `DatabaseHeader.SchemaCookie` update mechanism
- **File:** `src/Sharc.Core/Format/DatabaseHeader.cs` (MODIFY)
- **What:** When schema changes, increment change counter and schema cookie in page 1. Page 1 is special: first 100 bytes are the database header. BTreeMutator must update page 1 header bytes when schema changes.
- **Tests:** 3 tests — increment verified, page 1 modified, SQLite reads updated cookie

**Week 5: ~36 tests**

---

### Week 6 — Secondary Index Maintenance

#### Task 6.1: `IndexCellBuilder` — build index B-tree cells
- **File:** `src/Sharc.Core/BTree/IndexCellBuilder.cs` (NEW)
- **What:**
  - `BuildIndexLeafCell(ReadOnlySpan<byte> keyRecord, long rowId, Span<byte> destination) → int`
  - Index leaf cells contain: payload-size varint + key record (indexed columns encoded as record) + rowid appended to key
  - Must match format that `IndexBTreeCursor` / `IndexCellParser` already reads
- **Tests:** 6 tests in `tests/Sharc.Tests/Write/IndexCellBuilderTests.cs`
  - Single column index → cell parseable by IndexCellParser
  - Multi-column composite index → correct cell format
  - Round-trip: build → parse → same key values + rowid

#### Task 6.2: `IndexMaintainer` — auto-update secondary indexes on writes
- **File:** `src/Sharc/Write/IndexMaintainer.cs` (NEW)
- **What:** `internal sealed class IndexMaintainer`
  - Discovers all indexes for a table from schema
  - On INSERT: build index entry for each secondary index, insert into index B-tree
  - On UPDATE: delete old index entry, insert new entry
  - On DELETE: delete index entry
  - Uses `IndexCellBuilder` for cell construction, `BTreeMutator` for insertion
- **Tests:** 10 tests in `tests/Sharc.Tests/Write/IndexMaintainerTests.cs`
  - Insert row → index B-tree contains entry, navigable by IndexBTreeCursor
  - Insert 100 rows → all indexed, SeekFirst finds correct entries
  - Delete row → index entry removed
  - Update row → old index entry gone, new entry present
  - Composite index (2 columns) → correct key encoding
  - Table with no indexes → no index operations
  - Table with 3 indexes → all three updated per insert

#### Task 6.3: Wire IndexMaintainer into SharcWriter
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:** After table B-tree insert/update/delete, call IndexMaintainer for all affected indexes
- **Tests:** 8 integration tests in `tests/Sharc.IntegrationTests/IndexWriteTests.cs`
  - Insert rows → index seek returns correct rows
  - Delete row → index seek no longer finds it
  - SQLite reads Sharc-written indexes correctly
  - Insert with index → performance benchmark (≤ 2x slower than without)

**Week 6: ~24 tests**

---

## Phase 2 Exit Criteria

- [ ] 220 total new tests
- [ ] DELETE functional with freeblock management
- [ ] Graph edges writable via `Relate()`
- [ ] Secondary indexes auto-maintained on all writes
- [ ] `CreateTable` produces schema entries readable by SQLite
- [ ] Single-row auto-commit: ≤ 10 μs/row (maintained from Phase 1)
- [ ] Insert with 1 secondary index: ≤ 2x slower than no-index
- [ ] 5 benchmark gates passing
- [ ] All existing tests green

---

## Phase 3 — Advanced Features (2 weeks, ~50 tests)

### Week 7 — Checkpoint + Partial Update + Bulk Delete

#### Task 7.1: WAL Checkpoint
- **File:** `src/Sharc.Core/IO/WalCheckpointer.cs` (NEW)
- **What:** `internal sealed class WalCheckpointer`
  - `Checkpoint(string dbPath, string walPath)`:
    1. Acquire EXCLUSIVE lock
    2. Read WAL frame map (using existing WalReader)
    3. For each committed frame: copy page data from WAL into main DB file at correct offset
    4. fsync main DB file
    5. Reset WAL file (truncate or write new header)
    6. Release lock
  - Auto-checkpoint: trigger when WAL exceeds configurable frame count (default 1000)
- **Tests:** 10 tests in `tests/Sharc.Tests/Write/WalCheckpointerTests.cs`
  - Checkpoint single committed transaction → main DB updated
  - Checkpoint multiple transactions → all changes in main DB
  - After checkpoint, WAL empty/reset
  - Read after checkpoint (no WAL) → data still accessible
  - SQLite reads main DB after Sharc checkpoint → correct
  - Auto-checkpoint threshold triggers correctly

#### Task 7.2: `SharcWriter.Patch()` — partial field updates
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:**
  - `void Patch(string tableName, long rowId, params (string Column, ColumnValue Value)[] patches)`
  - Read-modify-write: read existing row, apply field changes, write back
  - Only specified columns change; others preserved
- **Tests:** 6 tests
  - Patch single field → only that field changed
  - Patch multiple fields → all changed, others preserved
  - Patch on non-existent rowid → throws

#### Task 7.3: `SharcWriter.DeleteWhere()` — filter-based bulk delete
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:**
  - `int DeleteWhere(string tableName, IFilterStar filter)` — returns count of deleted rows
  - Scan table with filter (uses Filter Engine from FilterActionPlan), delete each matching row
  - All deletes in single transaction
- **Depends on:** Filter Engine Phase 1 (FilterActionPlan.md)
- **Tests:** 6 tests
  - DeleteWhere matches subset → only matching rows deleted
  - DeleteWhere matches all → table empty
  - DeleteWhere matches none → no changes
  - DeleteWhere count matches actual deletions

#### Task 7.4: B-tree page merge/rebalance
- **File:** `src/Sharc.Core/BTree/BTreeMutator.cs` (MODIFY)
- **What:** When delete causes a page to become underfull (< 1/4 full):
  - Try to merge with a sibling page
  - If merged, remove parent interior cell and adjust parent
  - If siblings too full to merge, rebalance (move some cells from sibling)
- **Tests:** 8 tests
  - Delete enough rows to trigger merge → tree still valid
  - Rebalance: cells redistributed between siblings
  - After many deletes, tree height may shrink

**Week 7: ~30 tests**

---

### Week 8 — Performance Hardening + Benchmarks

#### Task 8.1: Batch insert optimization — pre-sort by rowid
- **File:** `src/Sharc/SharcWriter.cs` (MODIFY)
- **What:** In `InsertBatch()`, sort records by assigned rowid before insertion. Sequential key order minimizes page splits and maximizes page fill factor.
- **Tests:** 3 tests — unsorted input produces correct output, performance improvement measurable

#### Task 8.2: Page cache for write path
- **File:** `src/Sharc.Core/IO/PageManager.cs` (MODIFY)
- **What:** Cache recently read pages during write operations to avoid redundant reads of B-tree interior pages during bulk inserts
- **Tests:** 2 tests — cache hit avoids re-read, cache invalidated on write

#### Task 8.3: Overflow page writer
- **File:** `src/Sharc.Core/BTree/BTreeMutator.cs` (MODIFY)
- **What:** When record payload exceeds inline size, write excess to overflow pages. Chain overflow pages with 4-byte next-page pointer at start of each overflow page.
- **Tests:** 5 tests
  - Large record → overflow chain created
  - Read back via BTreeCursor → full payload assembled
  - Multiple overflow pages chained correctly
  - SQLite reads Sharc-written overflow records

#### Task 8.4: Comprehensive benchmark suite
- **File:** `bench/Sharc.Benchmarks/WriteBenchmarks.cs` (MODIFY)
- **What:** Full benchmark coverage:
  - `SingleRow_AutoCommit_WAL_SyncNormal` — gate: ≤ 10 μs/row
  - `Batch_1000Rows_SingleTransaction` — gate: ≤ 2.86 μs/row (350K/sec)
  - `Batch_100KRows_SingleTransaction` — gate: ≤ 1.67 μs/row (600K/sec)
  - `Allocation_SingleInsert` — gate: ≤ 300 B
  - `Sharc_vs_SQLite_Batch1000` — gate: Sharc ≤ 0.75x (25% faster)
  - `Insert_WithOneSecondaryIndex` — gate: ≤ 2x slower than no-index
  - `MixedReadWrite_80_20` — informational
  - `Delete_SingleRow` — informational
- **Tests:** 10 benchmark validation tests — each gate checked in test

**Week 8: ~20 tests**

---

## Phase 3 Exit Criteria

- [ ] 270 total new tests
- [ ] Checkpoint operational
- [ ] Partial updates (Patch) functional
- [ ] Bulk delete with filters (DeleteWhere) functional
- [ ] B-tree page merge/rebalance working
- [ ] Overflow pages on write path working
- [ ] Single-row auto-commit: ≤ 10 μs/row (≥ 100K rows/sec)
- [ ] Batch 1K rows: ≤ 2.86 μs/row (≥ 350K rows/sec)
- [ ] Batch 100K rows: ≤ 1.67 μs/row (≥ 600K rows/sec)
- [ ] Allocation per insert: ≤ 300 B
- [ ] vs SQLite (batch 1K): ≤ 0.75x (Sharc ≥ 25% faster)
- [ ] 8 benchmark gates passing — no regressions
- [ ] No memory leaks under sustained write load
- [ ] All existing tests green

---

## New File Summary

| File | Phase | Type |
|:---|:---:|:---|
| `src/Sharc.Core/Primitives/SerialTypeCodec.cs` | 1 | MODIFY — add GetSerialType |
| `src/Sharc.Core/Records/RecordEncoder.cs` | 1 | NEW — inverse of RecordDecoder |
| `src/Sharc.Core/BTree/CellBuilder.cs` | 1 | NEW — inverse of CellParser |
| `src/Sharc.Core/BTree/BTreeMutator.cs` | 1 | NEW — insert/update/delete + split |
| `src/Sharc.Core/BTree/IndexCellBuilder.cs` | 2 | NEW — index cell construction |
| `src/Sharc.Core/IPageStore.cs` | 1 | NEW — read-write page interface |
| `src/Sharc.Core/IO/PageManager.cs` | 1 | NEW — dirty page buffer + allocation |
| `src/Sharc.Core/IO/WalWriter.cs` | 1 | NEW — append WAL frames |
| `src/Sharc.Core/IO/WalCheckpointer.cs` | 3 | NEW — WAL → main DB transfer |
| `src/Sharc.Core/IO/WalReader.cs` | 1 | MODIFY — ComputeChecksum → internal |
| `src/Sharc.Core/Format/DatabaseHeader.cs` | 1 | MODIFY — add Write method |
| `src/Sharc.Core/Format/BTreePageHeader.cs` | 1 | MODIFY — add Write method |
| `src/Sharc.Core/Format/WalHeader.cs` | 1 | MODIFY — add Write method |
| `src/Sharc.Core/Format/WalFrameHeader.cs` | 1 | MODIFY — add Write method |
| `src/Sharc/SharcWriter.cs` | 1 | NEW — public write API |
| `src/Sharc/SharcTransaction.cs` | 1 | NEW — explicit transaction handle |
| `src/Sharc/ColumnDefinition.cs` | 2 | NEW — schema creation type |
| `src/Sharc/Write/TransactionManager.cs` | 1 | NEW — Begin/Commit/Rollback |
| `src/Sharc/Write/FileLock.cs` | 1 | NEW — SQLite-compatible locking |
| `src/Sharc/Write/IndexMaintainer.cs` | 2 | NEW — auto-update secondary indexes |

## Test File Summary

| File | Phase | Test Count |
|:---|:---:|---:|
| `tests/Sharc.Tests/Write/SerialTypeCodecWriteTests.cs` | 1 | ~15 |
| `tests/Sharc.Tests/Write/RecordEncoderTests.cs` | 1 | ~20 |
| `tests/Sharc.Tests/Write/CellBuilderTests.cs` | 1 | ~15 |
| `tests/Sharc.Tests/Write/HeaderWriteTests.cs` | 1 | ~12 |
| `tests/Sharc.Tests/Write/PageManagerTests.cs` | 1 | ~10 |
| `tests/Sharc.Tests/Write/WalWriterTests.cs` | 1 | ~13 |
| `tests/Sharc.Tests/Write/BTreeMutatorTests.cs` | 1 | ~30 |
| `tests/Sharc.Tests/Write/FileLockTests.cs` | 1 | ~5 |
| `tests/Sharc.Tests/Write/TransactionManagerTests.cs` | 1 | ~8 |
| `tests/Sharc.Tests/Write/BTreeMutatorDeleteTests.cs` | 2 | ~12 |
| `tests/Sharc.Tests/Write/IndexCellBuilderTests.cs` | 2 | ~6 |
| `tests/Sharc.Tests/Write/IndexMaintainerTests.cs` | 2 | ~10 |
| `tests/Sharc.Tests/Write/WalCheckpointerTests.cs` | 3 | ~10 |
| `tests/Sharc.IntegrationTests/WriteReadRoundTripTests.cs` | 1 | ~20 |
| `tests/Sharc.IntegrationTests/WriteDeleteTests.cs` | 2 | ~5 |
| `tests/Sharc.IntegrationTests/GraphWriteTests.cs` | 2 | ~8 |
| `tests/Sharc.IntegrationTests/SchemaWriteTests.cs` | 2 | ~8 |
| `tests/Sharc.IntegrationTests/IndexWriteTests.cs` | 2 | ~8 |
| `bench/Sharc.Benchmarks/WriteBenchmarks.cs` | 1+ | Benchmarks |

---

## Interoperability Guarantee

**Non-negotiable:** Every database file written by Sharc must be openable and fully readable by stock SQLite (≥ 3.7.0).

Verified by:
1. **Binary format compliance** — every header, serial type, varint, cell, page layout, WAL frame, checksum follows SQLite spec exactly
2. **Round-trip integration tests** — write with Sharc → read with `Microsoft.Data.Sqlite` → verify equality
3. **Fuzz testing** — random ColumnValues (NULL, empty string, zero-length blob, max int64, NaN double, multi-byte UTF-8) through write → read cycle
4. **Mixed-mode** — SQLite writes 500 → Sharc writes 500 → both read 1000

---

## Risk Assessment

| Risk | Severity | Mitigation |
|:---|:---|:---|
| B-tree page split produces corrupt tree | Critical | Invariant checks after every split: sorted order, depth balance, parent-child consistency. Compare against SQLite output for identical data. |
| WAL checksum mismatch → SQLite rejects | Critical | Reuse exact `ComputeChecksum` from read path. Write → read-back test on every frame. |
| File lock incompatible with SQLite | High | Test: Sharc writer + SQLite reader, SQLite writer + Sharc reader. Same byte ranges. |
| Overflow pages not chained correctly | High | Test with records > page size. Verify chain navigable by existing BTreeCursor. |
| Freelist corruption | High | Single-writer eliminates concurrent allocation. Freelist changes are part of transaction dirty pages. |
| Performance regression on read path | Medium | Write engine is separate code. Read path unchanged. Benchmark reads before and after merge. |
| Schema `sqlite_master` format mismatch | Medium | Use `CreateTableParser` (exists) to validate generated SQL parses identically. |

---

## Dependencies Between Plans

```
FilterActionPlan Phase 1 (Weeks 1-3)
    ↓ no dependency
WriteActionPlan Phase 1 (Weeks 1-4)
WriteActionPlan Phase 2 (Weeks 5-6)
    ↓ DeleteWhere needs filter engine
FilterActionPlan Phase 1 ──→ WriteActionPlan Phase 3, Task 7.3 (DeleteWhere)
```

The two plans can proceed in parallel through Phase 1 and Phase 2. Only `WriteActionPlan Phase 3 Task 7.3 (DeleteWhere)` depends on the Filter Engine being available.

---

## Success Metrics

| Metric | Phase 1 | Phase 2 | Phase 3 |
|:---|---:|---:|---:|
| Write operations | INSERT, UPDATE, UPSERT | + DELETE, RELATE, CreateTable | + Patch, DeleteWhere, Checkpoint |
| SurrealDB coverage | 5/11 (45%) | 8/11 (73%) | 11/11 (100%) |
| Single-row insert | ≤ 12 μs/row | ≤ 10 μs/row | ≤ 10 μs/row |
| Batch 1K rows | ≤ 4 μs/row | ≤ 3 μs/row | ≤ 2.86 μs/row |
| Batch 100K rows | ≤ 2.5 μs/row | ≤ 2 μs/row | ≤ 1.67 μs/row |
| Alloc per insert | ≤ 4.5 KB | ≤ 1 KB | ≤ 300 B |
| vs SQLite batch 1K | ≤ 1.0x | ≤ 0.85x | ≤ 0.75x |
| New tests | 150 | 220 | 270 |
| Benchmark gates | 3 | 5 | 8 |
| Read-path regression | 0% | 0% | 0% |
