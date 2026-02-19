# Architecture Overview — Sharc

## 1. Design Philosophy

Sharc is a **layered, interface-driven SQLite file-format reader and writer**. Each layer has a single responsibility, communicates through well-defined interfaces, and is independently testable. The library includes a cryptographic trust layer for AI multi-agent coordination.

Core tenets:
- **Composition over inheritance** — layers compose via interfaces, never via class hierarchies
- **Spans over arrays** — data flows as `ReadOnlySpan<byte>` through the stack
- **No hidden I/O** — all file access goes through `IPageSource`
- **No hidden allocations** — hot paths are allocation-free; cold paths use pooling
- **Fail fast, fail clearly** — corrupt data raises typed exceptions immediately

## 2. Layer Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  CONSUMER LAYER                                                 │
│                                                                 │
│  SharcDatabase.Open(path) / OpenMemory(buffer)                  │
│       │                                                         │
│       ▼                                                         │
│  SharcDatabase ──→ SharcSchema (tables, indexes, views)         │
│       │                                                         │
│       ▼                                                         │
│  SharcDataReader ──→ Read() / GetInt64() / GetString()          │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  SCHEMA LAYER (Sharc.Core/Schema/)                              │
│       │                                                         │
│  SchemaReader                                                   │
│    - reads sqlite_schema from page 1 b-tree                     │
│    - parses CREATE TABLE SQL to extract column definitions       │
│    - builds TableInfo / ColumnInfo / IndexInfo / ViewInfo        │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  RECORD LAYER (Sharc.Core/Records/)                             │
│       │                                                         │
│  RecordDecoder (implements IRecordDecoder)                       │
│    - reads record header (varint-encoded column serial types)    │
│    - reads column values from body according to serial types     │
│    - returns ColumnValue[] or single ColumnValue                 │
│    - operates entirely on ReadOnlySpan<byte>                     │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  B-TREE LAYER (Sharc.Core/BTree/)                               │
│       │                                                         │
│  BTreeReader (implements IBTreeReader)                           │
│    - creates BTreeCursor for a given root page                   │
│    - handles interior → leaf page traversal (depth-first)        │
│       │                                                         │
│  BTreeCursor (implements IBTreeCursor)                           │
│    - iterates leaf cells in rowid order                          │
│    - extracts rowid + payload from table leaf cells              │
│    - follows overflow page chains for large payloads             │
│       │                                                         │
│  CellParser                                                     │
│    - parses cell structure within a page                         │
│    - calculates inline vs overflow payload boundaries            │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  PAGE I/O LAYER (Sharc.Core/IO/)                                │
│       │                                                         │
│  IPageSource                                                    │
│    ├── FilePageSource    — reads pages from FileStream           │
│    ├── MemoryPageSource  — reads pages from ReadOnlyMemory<byte> │
│    └── CachedPageSource  — LRU cache wrapping any IPageSource    │
│       │                                                         │
│  IPageTransform                                                 │
│    ├── IdentityPageTransform    — no-op (unencrypted)            │
│    └── DecryptingPageTransform  — AES-256-GCM decryption         │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  PRIMITIVES (Sharc.Core/Primitives/)                            │
│       │                                                         │
│  VarintDecoder  — SQLite variable-length integer encoding        │
│  SerialTypeCodec — serial type → storage class + byte length     │
│       │                                                         │
└───────┼─────────────────────────────────────────────────────────┘
        │
┌───────┼─────────────────────────────────────────────────────────┐
│  FORMAT (Sharc.Core/Format/)                                    │
│       │                                                         │
│  DatabaseHeader   — 100-byte file header parsing                 │
│  BTreePageHeader  — b-tree page header + cell pointer array      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## 3. Data Flow — Reading a Table Row

```
User calls: reader.Read()
    │
    ▼
SharcDataReader asks BTreeCursor.MoveNext()
    │
    ▼
BTreeCursor checks current page for more cells
    │
    ├── YES: advance cell pointer index
    │
    └── NO: pop page stack, traverse to next leaf via parent interior pages
            │
            ▼
        BTreeCursor asks IPageSource.GetPage(nextPageNumber)
            │
            ▼
        CachedPageSource checks LRU cache
            │
            ├── HIT: return cached page span
            │
            └── MISS: ask inner IPageSource (File or Memory)
                    │
                    ▼
                FilePageSource seeks to (pageNumber - 1) * pageSize, reads pageSize bytes
                    │
                    ▼
                (Optional) IPageTransform.TransformRead() decrypts page
                    │
                    ▼
                Returns page span to BTreeCursor
    │
    ▼
BTreeCursor parses cell at current pointer offset:
    - reads payload size varint
    - reads rowid varint
    - extracts inline payload span
    - if overflow: follows overflow page chain, assembles full payload
    │
    ▼
SharcDataReader calls RecordDecoder.DecodeRecord(payload)
    │
    ▼
RecordDecoder:
    - reads header size varint
    - reads per-column serial type varints
    - for each column: reads value bytes from body, constructs ColumnValue
    │
    ▼
SharcDataReader exposes column values via GetInt64(), GetString(), etc.
```

## 4. Assembly Dependencies

```
Sharc (public API + Trust)
  └── Sharc.Core (internal engine + write + trust models)

Sharc.Graph (graph storage)
  ├── Sharc.Core
  └── Sharc.Graph.Surface (graph models)

Sharc.Crypto (encryption)
  └── Sharc.Core (IPageTransform)

Sharc.Scene (trust playground)
  └── Sharc (Trust layer)

Sharc.Tests
  ├── Sharc
  ├── Sharc.Core
  └── Sharc.Crypto

Sharc.Benchmarks
  ├── Sharc
  └── Sharc.Core
```

Sharc.Crypto is **optional** — unencrypted databases require only Sharc + Sharc.Core.
Sharc.Graph is **optional** — graph storage adds ConceptStore/RelationStore over the B-tree engine.
Sharc.Scene is **optional** — trust playground for visualizing agent interactions.

## 5. Threading Model

- `SharcDatabase` is **thread-safe for schema access** (schema is immutable after open)
- `SharcDataReader` is **not thread-safe** — one reader per thread
- `IPageSource` implementations must be **thread-safe for reads** (multiple cursors may read concurrently)
- `CachedPageSource` uses lock-free or reader-writer locking for cache access
- `FilePageSource` uses `FileStream` with `FileOptions.RandomAccess` — OS handles concurrent reads

## 6. Memory Model

### Ownership Rules

| Object | Owns | Lifetime |
|--------|------|----------|
| `SharcDatabase` | `IPageSource`, `SchemaReader`, page cache | Until `Dispose()` |
| `SharcDataReader` | `IBTreeCursor`, current record buffer | Until `Dispose()` |
| `IBTreeCursor` | Page stack, overflow assembly buffer | Until `Dispose()` |
| `CachedPageSource` | Page cache (demand-driven byte arrays from pool) | Until `Dispose()` |
| `SharcKeyHandle` | Pinned key memory | Until `Dispose()` |

### Allocation Budget

| Operation | Target |
|-----------|--------|
| Open database | O(1) allocations (header parse, schema read is cold path) |
| Read next row | 0 allocations (span-based decode) |
| Get integer column | 0 allocations |
| Get string column | 1 allocation (string creation) |
| Get blob column | 1 allocation (byte[] copy) or 0 (span access) |
| Overflow page assembly | 1 pooled buffer (returned after use) |

## 7. Extension Points

| Interface | Purpose | Built-in Implementations |
|-----------|---------|------------------------|
| `IPageSource` | Page I/O backend | `FilePageSource`, `MemoryPageSource`, `CachedPageSource` |
| `IPageTransform` | Page pre/post processing | `IdentityPageTransform`, `DecryptingPageTransform` |
| `IBTreeReader` | B-tree access strategy | `BTreeReader` |
| `IRecordDecoder` | Record format interpretation | `RecordDecoder` |

All interfaces are in `Sharc.Core` and are `internal` by default. They exist for testability and future extensibility, not for consumer use.

## 8. Configuration Surface

All configuration flows through `SharcOpenOptions`:

```csharp
new SharcOpenOptions
{
    PageCacheSize = 2000,       // LRU cache capacity (0 = disabled)
    PreloadToMemory = false,    // Read entire file on open
    FileShareMode = FileShare.ReadWrite,  // Coexist with SQLite writers
    Encryption = new SharcEncryptionOptions  // null for unencrypted
    {
        Password = "...",
        Kdf = SharcKdfAlgorithm.Argon2id,
        Cipher = SharcCipherAlgorithm.Aes256Gcm
    }
}
```

## 9. Completed Post-MVP Architecture

### WAL Support (Milestone 8 — COMPLETE)

- `WalPageSource` merges WAL frames with main database pages
- WAL index (shm) parsing for frame lookup
- Snapshot reads at a consistent point via frame-by-frame replay

### WHERE Filtering (Milestone 7 — COMPLETE)

- `SharcFilter` with 6 operators (Eq, Ne, Lt, Le, Gt, Ge) across all column types
- **Filter Star (JIT)**: Flattened Expression Trees with **Offset Hoisting**
- Scan-based JIT filtering outperforms SQLite VDB by ~25%
- Composable with column projection for <2KB total allocation per 5k row scan

### Index Reads (Milestone 7+ — COMPLETE)

- `IndexBTreeCursor` for index b-tree traversal
- Key comparison using SQLite collation rules
- `WithoutRowIdCursorAdapter` for WITHOUT ROWID table support

### Encryption (Milestone 9 — COMPLETE)

- `AesGcmPageTransform` implements `IPageTransform` for page-level AES-256-GCM decryption
- `DecryptingPageSource` wraps any `IPageSource` with transparent decryption
- `SharcKeyHandle` with Argon2id and PBKDF2-SHA512 key derivation
- `EncryptionHeader` parsed from reserved space in page 1

### Graph Storage (Phase 2 — COMPLETE)

- `Sharc.Graph` layers concept/relation graph model over B-tree storage
- `ConceptStore` and `RelationStore` with O(log N) index seeks
- Two-phase BFS in `Traverse()`: edge-only discovery then batch node lookup (31x faster than SQLite)
- Zero-allocation `IEdgeCursor` with `GetEdgeCursor()` + `Reset()` for multi-hop traversal
- `TraversalPolicy` enforcement: TargetTypeFilter, MaxTokens, Timeout, StopAtKey, IncludePaths
- `GetEdges()`/`GetIncomingEdges()` marked `[Obsolete]` in favor of cursor/Traverse APIs
- Schema adapter resolves table root pages dynamically

### Write Engine (Phase 3 — IN PROGRESS)

- `SharcWriter` public API for INSERT operations
- `BTreeMutator` handles leaf inserts with B-tree page splits
- `RecordEncoder` serializes typed values to SQLite record format
- `CellBuilder` constructs B-tree leaf cells from encoded records
- `PageManager` allocates and manages database pages
- `RollbackJournal` provides ACID transaction support (atomic writes, rollback)
- `Transaction` wraps Begin/Commit/Rollback lifecycle

### Agent Trust Layer (Phase 4 — COMPLETE)

- `AgentRegistry` manages agent registration with ECDSA self-attestation
- `LedgerManager` provides hash-chain audit log (SHA-256 linked, ECDSA signed)
- `ReputationEngine` tracks agent scoring based on ledger activity
- Co-signature support for multi-agent endorsement
- Governance policies for permission boundaries and scope control
- Trust models (`AgentInfo`, `AgentClass`, `TrustPayload`, `LedgerEntry`) in `Sharc.Core.Trust`
- `Sharc.Scene` trust playground for live agent simulation and visualization

### Current Test Status

**2,216 tests passing** across 6 projects (unit + integration + query + graph + index + context).
