# Execution Plan — Sharc

## Milestone Overview

| # | Milestone | Key Deliverable | Gate Criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Primitives + Spans | Varint, serial types, header parsing | All unit tests green, zero allocations in hot paths | **COMPLETE** |
| 2 | Page I/O + Cache | IPageSource (file + memory + mmap), LRU cache | Read any page from real .db file | **COMPLETE** |
| 3 | B-Tree Reader | Table b-tree traversal, overflow pages | Enumerate all cells in a table | **COMPLETE** |
| 4 | Record Decoder | Full record to typed column values | Decode all SQLite serial types correctly | **COMPLETE** |
| 5 | Schema Reader | Parse sqlite_schema, expose table list | List all tables/indexes from real DB | **COMPLETE** |
| 6 | Table Scans | SharcDatabase + SharcDataReader API | Read all rows from any table in real DB | **COMPLETE (MVP)** |
| 7 | SQL Subset (Optional) | Simple WHERE filtering | Basic equality/comparison filtering | Future |
| 8 | WAL Read Support | Read WAL-mode databases | Correctly merge WAL frames with main DB | Future |
| 9 | Encryption | AES-256-GCM page-level crypto | Open and read encrypted DB | Future |
| 10 | Benchmarks | BenchmarkDotNet suite, allocation audit | Comparative suite per BenchmarkSpec.md | **COMPLETE** |

## Detailed Milestone Breakdown

### Milestone 1: Primitives + Spans
**Tests first:**
- VarintDecoderTests (15+ test cases)
- SerialTypeTests (12+ test cases)
- DatabaseHeaderTests (5+ test cases)
- BTreePageHeaderTests (8+ test cases)

**Implementation:**
- `Sharc.Core/Primitives/VarintDecoder.cs`
- `Sharc.Core/Primitives/SerialTypeCodec.cs`
- `Sharc.Core/Format/DatabaseHeader.cs`
- `Sharc.Core/Format/BTreePageHeader.cs`

**Gate:** `dotnet test` all green. Benchmark varint decode > 100M ops/sec.

### Milestone 2: Page I/O + Cache
**Tests first:**
- FilePageSourceTests
- MemoryPageSourceTests
- PageCacheTests

**Implementation:**
- `Sharc.Core/IO/FilePageSource.cs`
- `Sharc.Core/IO/MemoryPageSource.cs`
- `Sharc.Core/IO/PageCache.cs`
- `Sharc.Core/IO/IPageSource.cs`

**Gate:** Read any page by number from file and memory. Cache hit rate measurable.

### Milestone 3: B-Tree Reader
**Tests first:**
- BTreeReaderTests (leaf enumeration, interior traversal, overflow)

**Implementation:**
- `Sharc.Core/BTree/BTreeReader.cs`
- `Sharc.Core/BTree/BTreeCursor.cs`
- `Sharc.Core/BTree/CellParser.cs`

**Gate:** Enumerate all cells from sqlite_schema table of a real database.

### Milestone 4: Record Decoder
**Tests first:**
- RecordDecoderTests (all serial types, multi-column, edge cases)

**Implementation:**
- `Sharc.Core/Records/RecordDecoder.cs`
- `Sharc.Core/Records/ColumnValue.cs`

**Gate:** Decode every row in sqlite_schema into typed values.

### Milestone 5: Schema Reader
**Tests first:**
- SchemaReaderTests (table list, index list, column metadata)

**Implementation:**
- `Sharc.Core/Schema/SchemaReader.cs`
- `Sharc/Schema/TableInfo.cs`
- `Sharc/Schema/ColumnInfo.cs`
- `Sharc/Schema/IndexInfo.cs`

**Gate:** Programmatic access to all tables and their column definitions.

### Milestone 6: Table Scans (MVP!)
**Tests first:**
- SharcDatabaseTests (open, enumerate, read)
- SharcDataReaderTests (typed column access)

**Implementation:**
- `Sharc/SharcDatabase.cs` (full implementation)
- `Sharc/SharcDataReader.cs` (full implementation)
- Wire up all layers

**Gate:** Complete read of any table in any standard SQLite database.
This is the **minimum viable product** milestone.

### Milestones 7–10: See per-milestone design docs (created when reached)

## Dependency Graph

```
[1: Primitives] ──→ [2: Page I/O] ──→ [3: B-Tree] ──→ [6: Table Scans]
                                            ↓                ↑
                                       [4: Records] ────────┘
                                            ↓
                                       [5: Schema] ────→ [6]
                                                          ↓
                                                     [7: SQL]
                                                     [8: WAL]
                                                     [9: Encryption]
                                                     [10: Benchmarks]
```

## Time Estimates (Working Days)

| Milestone | Estimate | Cumulative |
|-----------|----------|-----------|
| 1 | 2d | 2d |
| 2 | 2d | 4d |
| 3 | 3d | 7d |
| 4 | 2d | 9d |
| 5 | 1d | 10d |
| 6 | 2d | 12d |
| 7 | 3d | 15d |
| 8 | 3d | 18d |
| 9 | 3d | 21d |
| 10 | 2d | 23d |
