# Execution Plan — Sharc

## Milestone Overview

| # | Milestone | Key Deliverable | Gate Criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Primitives + Spans | Varint, serial types, header parsing | All unit tests green | **COMPLETE** |
| 2 | Page I/O + Cache | IPageSource, LRU cache | Read from real .db file | **COMPLETE** |
| 3 | B-Tree Reader | Table b-tree traversal, overflow pages | Enumerate all cells | **COMPLETE** |
| 4 | Record Decoder | Full record to typed column values | Decode all serial types | **COMPLETE** |
| 5 | Schema Reader | Parse sqlite_schema | List all tables/indexes | **COMPLETE** |
| 6 | Table Scans | SharcDatabase + SharcDataReader API | Read all rows from any table | **COMPLETE** |
| 7 | **Graph Storage** | ConceptStore, RelationStore, Seek navigation | O(log N) point lookups | **COMPLETE (PHASE 2)** |
| 8 | **Optimization** | WASM Trimming + Native AOT support | Under 500KB binary size | **COMPLETE (PHASE 2)** |
| 9 | **Benchmarking** | Comparison vs. SurrealDB | Verified performance wins | **IN PROGRESS (PHASE 3)** |
| 10 | **Write Support** | B-Tree cell insertion and WAL writing | Append-only graph growth | **PLANNED** |

## Phase 2: Graph & AOT (COMPLETED)
- Implemented `Sharc.Graph` library with `ConceptStore` and `RelationStore`.
- Added binary-search `Seek` to `BTreeCursor` for O(log N) rowid lookups.
- Globally enabled `<IsTrimmable>` and `<IsAotCompatible>` for source projects.
- Documented "MakerGraph" schema for AI context compression.

## Phase 3: Performance & Benchmarking (CURRENT)
- **Sharc.Comparisons**: Benchmark project comparing Sharc.Graph vs. SurrealDB.
- **Iteration**: Profiling B-Tree navigation to minimize branch mispredictions and memory stalls.
- **Context API**: High-level `IContextGraph` orchestrator for easy AI integration.
