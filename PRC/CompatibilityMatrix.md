# Compatibility Matrix — Sharc

## SQLite Feature Support Status

### Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Supported in current version |
| 🔶 | Planned for a specific milestone |
| ❌ | Not planned / out of scope |
| ⚠️ | Partial support with caveats |

---

## File Format

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Format 3 magic validation | ✅ | M1 | |
| Page sizes 512–65536 | ✅ | M1 | Including value-1-means-65536 |
| Page size = power of 2 validation | ✅ | M1 | |
| Reserved bytes per page | ✅ | M1 | Usable = PageSize - Reserved |
| Schema format 4 | ✅ | M1 | Default for modern SQLite |
| Schema format 1–3 | ⚠️ | M1 | Parsed but may lack features |
| Big-endian header fields | ✅ | M1 | Via BinaryPrimitives |
| File change counter | ✅ | M2 | Read, not written |
| Schema cookie | ✅ | M5 | Read, not written |

## Text Encoding

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| UTF-8 (encoding = 1) | ✅ | M1 | Primary target |
| UTF-16LE (encoding = 2) | 🔶 | Post-MVP | Architecture supports it |
| UTF-16BE (encoding = 3) | 🔶 | Post-MVP | Architecture supports it |

## Page Types

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Table leaf (0x0D) | ✅ | M3 | Core functionality |
| Table interior (0x05) | ✅ | M3 | Core functionality |
| Index leaf (0x0A) | ✅ | M7 | Index B-tree reads |
| Index interior (0x02) | ✅ | M7 | Index B-tree reads |
| Freelist trunk pages | ❌ | — | Write/compact only |
| Freelist leaf pages | ❌ | — | Write/compact only |
| Overflow pages | ✅ | M3 | Following overflow chains |
| Pointer map pages | ❌ | — | Auto-vacuum only |
| Lock byte page | ❌ | — | Not needed for reads |

## B-Tree Operations

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Table b-tree sequential scan | ✅ | M3 | Full table scan |
| Table b-tree rowid lookup | ✅ | M7 | Binary search via Seek API |
| Index b-tree sequential scan | ✅ | M7 | Via IndexBTreeCursor |
| Index b-tree key lookup | ✅ | M7 | Via IndexBTreeCursor |
| Overflow page following | ✅ | M3 | Linked list traversal |
| Cell pointer array reading | ✅ | M3 | |

## Record Format

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Varint decoding (1–9 bytes) | ✅ | M1 | |
| Serial type 0 (NULL) | ✅ | M4 | |
| Serial types 1–6 (integers) | ✅ | M4 | 8/16/24/32/48/64-bit |
| Serial type 7 (float) | ✅ | M4 | IEEE 754 double |
| Serial type 8 (constant 0) | ✅ | M4 | |
| Serial type 9 (constant 1) | ✅ | M4 | |
| Serial types ≥12 (BLOB) | ✅ | M4 | Even types |
| Serial types ≥13 (TEXT) | ✅ | M4 | Odd types |
| Serial types 10, 11 (reserved) | ⚠️ | M4 | Throws error (ADR-007) |
| Multi-column records | ✅ | M4 | |
| Records spanning overflow | ✅ | M4 | Via assembled payload |

## Schema

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| sqlite_schema table reading | ✅ | M5 | Page 1 b-tree |
| Table enumeration | ✅ | M5 | |
| Index enumeration | ✅ | M5 | |
| View enumeration | ✅ | M5 | |
| Trigger enumeration | ⚠️ | M5 | Listed but not executable |
| Column name extraction | ✅ | M5 | From CREATE TABLE SQL |
| Column type extraction | ✅ | M5 | Declared type string |
| PRIMARY KEY detection | ✅ | M5 | |
| NOT NULL detection | ✅ | M5 | |
| DEFAULT values | 🔶 | Post-MVP | Parsed from SQL |
| CHECK constraints | ❌ | — | Not enforced (read-only) |
| FOREIGN KEY info | 🔶 | Post-MVP | Parsed from SQL |
| Auto-increment detection | 🔶 | Post-MVP | |

## Journal / WAL

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Legacy rollback journal mode | ✅ | M2 | Default; journal file ignored |
| WAL mode detection | ✅ | M1 | Header flag read |
| WAL file reading | ✅ | M8 | Frame-by-frame merge |
| WAL checkpointing | ❌ | — | Write operation |
| WAL index (shm) reading | ✅ | M8 | For consistent snapshots |
| DELETE journal mode | ✅ | M2 | Journal file not read |
| TRUNCATE journal mode | ✅ | M2 | Journal file not read |
| PERSIST journal mode | ✅ | M2 | Journal file not read |
| MEMORY journal mode | ✅ | M2 | No journal file exists |
| OFF journal mode | ✅ | M2 | No journal file exists |

## Table Types

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Regular tables (rowid) | ✅ | M6 | Core functionality |
| WITHOUT ROWID tables | ✅ | M7+ | Via WithoutRowIdCursorAdapter wrapping IndexBTreeCursor |
| STRICT tables | ✅ | M6 | Type enforcement is SQLite's concern |
| Virtual tables (FTS) | ❌ | — | Requires module code |
| Virtual tables (R-Tree) | ❌ | — | Requires module code |
| Virtual tables (JSON) | ❌ | — | Requires module code |
| Shadow tables (for FTS etc.) | ⚠️ | M6 | Readable as regular tables |

## SQL / Query

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Full SQL parsing | ❌ | — | Out of scope |
| SQL VM / VDBE | ❌ | — | Out of scope |
| Query planner | ❌ | — | Out of scope |
| Simple WHERE filtering | ✅ | M7+ | SharcFilter + FilterEvaluator (6 operators, all types) |
| ORDER BY | ❌ | — | Rows returned in rowid order |
| GROUP BY / aggregates | ❌ | — | Consumer's responsibility |
| JOIN | ❌ | — | Consumer's responsibility |
| Subqueries | ❌ | — | Out of scope |
| User-defined functions | ❌ | — | Out of scope |
| Collation sequences | ⚠️ | M6 | BINARY only; NOCASE deferred |

## Encryption

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Sharc encryption format | ✅ | M9 | 128-byte header, magic + KDF params + salt + verification hash |
| AES-256-GCM | ✅ | M9 | Default cipher, deterministic HMAC nonce per page |
| XChaCha20-Poly1305 | 🔶 | Post-M9 | Alternative cipher |
| Argon2id KDF | ✅ | M9 | PBKDF2-SHA512 bridge, Argon2id v0.2 planned |
| scrypt KDF | 🔶 | Post-M9 | Alternative KDF |
| Page-level decryption | ✅ | M9 | Via AesGcmPageTransform + DecryptingPageSource |
| Row-level entitlement crypto | ⚠️ | Post-M9 | HKDF-SHA256 scaffolded, wiring deferred |
| SQLCipher compatibility | ❌ | — | Different format entirely |
| SEE compatibility | ❌ | — | Proprietary format |

## Concurrency & Access

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| File read sharing (FileShare.ReadWrite) | ✅ | M2 | Coexist with SQLite writers |
| Multiple readers on same SharcDatabase | ✅ | M6 | Thread-safe schema + page source |
| Snapshot isolation | ✅ | M8 | Via change counter / WAL frame reads |
| Write transactions | ❌ | — | Read-only library |


## Graph Engine (Sharc.Graph)

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Node storage (native) | ✅ | M10 | Store arbitrary JSON+Vector on nodes |
| Edge storage (native) | ✅ | M10 | Directed edges with "Kind" property |
| Adjacency Index | ✅ | M10 | O(log N) traversal in both directions |
| BFS Traversal | ✅ | M10 | `Graph.Traverse` with depth limits |
| Subgraph extraction | 🔶 | Post-MVP | Extract self-contained neighborhood |
| Vector similarity search | ❌ | — | Requires external vector index (for now) |

## Trust Layer (Sharc.Trust)

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| SHA-256 Hash Chain | ✅ | M11 | Tamper-evident linked list |
| ECDsa P-256 Signatures | ✅ | M11 | NIST standard curves |
| Structured Payloads | ✅ | M11 | JSON payloads with type discrimination |
| Co-signing | ✅ | M11 | Multi-party approval on single payload |
| Agent Registry | ✅ | M11 | On-chain identity management |
| Authority Ceilings | ✅ | M11 | Enforced spending/action limits |
| Evidence Linking | ✅ | M11 | Cryptographic reference to source rows |
| Cross-Database Sync | ✅ | M11 | `ExportDeltas` / `ImportDeltas` |



| Platform | Status | Notes |
|----------|--------|-------|
| .NET 10 (Windows x64) | ✅ | Primary target (current) |
| .NET 10 (Linux x64) | ✅ | Primary target |
| .NET 10 (macOS ARM64) | ✅ | Primary target |
| .NET 10 (Linux ARM64) | ✅ | |
| .NET 8/9 | ✅ | Backward-compatible |
| Blazor WebAssembly | ⚠️ | Memory-backed only, no file I/O, no AES-NI |
| .NET Framework 4.x | ❌ | Requires .NET 8+ for Span/ReadOnlySpan support |
| .NET Standard 2.0/2.1 | ❌ | Too restrictive for span-heavy code |
