# Recall Identity Policy

*Status: Active — Governs all identity, fingerprinting, and deduplication decisions.*

## Motivation

Sharc currently uses `Fingerprint128` (128-bit FNV-1a dual-lane hash) as its sole row-identity mechanism for UNION/INTERSECT/EXCEPT dedup and GROUP BY text pooling. This works — collision probability is P ≈ N²/2⁹⁷ ≈ 10⁻¹⁶ at 6M rows — but it's expensive when cheaper alternatives exist, and it's insufficient for distributed scenarios where agents process sharded ledgers across files.

This document establishes a **tiered identity model** that matches identity cost to use-case needs, and introduces **database-level** and **table-level** GUIDs for future distributed sharding.

---

## Identity Tiers

### Tier 0 — Inherent Identity (Free)

**What:** The combination of structural position that the row already occupies.

| Signal | Source | Cost |
|:-------|:-------|:-----|
| Root page number | `TableInfo.RootPage` (uint) | Already resolved at schema load |
| Row ID | SQLite implicit INTEGER PRIMARY KEY (long) | Already decoded by B-tree cursor |

**When to use:** Single-table operations where the row is identified by its table + rowid. This is the identity that `PreparedReader.Seek(rowid)` already uses — no hash needed.

**Uniqueness scope:** Unique within a single database file. Not unique across files (two different `.db` files can both have rowid 42 in their `users` table).

**Where it applies today:**
- `SharcDataReader.Seek(rowId)` — B-tree point lookup by rowid
- `SharcWriter.Delete(rowId)` / `Update(rowId, ...)` — mutation by rowid
- `PreparedWriter.Delete(rowId)` / `Update(rowId, ...)` — prepared mutation

### Tier 1 — Positional Identity (Cheap)

**What:** The physical page + cell position of a row in the B-tree.

| Signal | Source | Cost |
|:-------|:-------|:-----|
| Page number | `IBTreeCursor.CurrentPage` (uint) | Available during traversal |
| Cell index | `IBTreeCursor.CurrentCellIndex` (int) | Available during traversal |
| Payload offset | `IBTreeCursor.Payload.Offset` | Available during traversal |

**When to use:** Cursor-level bookmarking, resumable traversals, page-local dedup. Positional identity is **unstable** — it changes after INSERT/DELETE/VACUUM, so it can only be used within a single read transaction's lifetime.

**Uniqueness scope:** Unique within a single read snapshot. Not durable across mutations.

**Where it applies today:**
- `LeafPageScanner` position tracking
- `BTreeCursor.Reset()` for prepared pattern reuse
- Index seek cursor positioning

### Tier 2 — Value Identity (O(bytes), Zero-Alloc)

**What:** Content-addressed identity via `Fingerprint128` — a 128-bit hash of the row's projected column values plus structural metadata.

| Component | Bits | Purpose |
|:----------|:-----|:--------|
| `Lo` | 64 | Primary FNV-1a hash (bucket selection + comparison) |
| `Guard` | 32 | Secondary FNV-1a hash (collision resistance → 96-bit total) |
| `PayloadLen` | 16 | Byte-length fingerprint (instant structural rejection) |
| `TypeTag` | 16 | Column-type signature (structural fingerprint) |

**When to use:** Cross-cursor deduplication where rowids are meaningless — different tables, different projections, different files. This is the **only tier that works for set operations** (UNION/INTERSECT/EXCEPT) because rows from different result sets have no shared structural identity.

**Cost:** O(bytes) per row — iterates every projected column byte through dual FNV-1a. Zero managed allocation (`Fnv1aHasher` is a `ref struct`, stack-only).

**Uniqueness scope:** Probabilistically unique across all rows everywhere. Content-addressed — identical column values produce identical fingerprints regardless of source table, file, or rowid.

**Where it applies today:**
- `SharcDataReader.GetRowFingerprint()` — UNION/INTERSECT/EXCEPT dedup
- `SharcDataReader.GetColumnFingerprint()` — GROUP BY text string pooling
- `IndexSet` — open-addressing hash set for right-side pre-scan
- `CompoundQueryExecutor.ExecuteIndexSetOp()` — streaming set operations
- `StreamingAggregateProcessor` — text key dedup during aggregation

### Tier 3 — Table Identity (GUID, Cold Path)

**What:** A persistent, unique identifier for each table within a database file.

**Representation:** 128-bit GUID stored as `_sharc_table_guid` in a Sharc metadata table, keyed by table name.

| Field | Type | Purpose |
|:------|:-----|:--------|
| `TableName` | TEXT PRIMARY KEY | The table this GUID identifies |
| `TableGuid` | BLOB(16) | RFC 4122 v4 GUID, generated once at table creation |

**When to use:** Cross-file row identity for distributed scenarios. A globally unique row reference becomes `(FileGuid, TableGuid, RowId)` — a 40-byte tuple that uniquely identifies any row across any Sharc database anywhere.

**Uniqueness scope:** Globally unique across all Sharc database files, all agents, all time.

**Where it will apply:**
- Agent ledger sync — when agent A references a row in file X, agent B on file Y can resolve the reference unambiguously
- Distributed sharding — shard coordinator maps `TableGuid` to physical file location
- Cross-file UNION — dedup by `(TableGuid, RowId)` instead of value hashing when both sides are Sharc files

**Implementation notes:**
- Generated via `Guid.NewGuid()` (RFC 4122 v4, 122 bits of randomness)
- Stored using existing `GuidCodec` (big-endian BLOB(16), serial type 44)
- Assigned once at table creation time; immutable thereafter
- Vacuuming, compacting, or copying the file preserves table GUIDs
- Tables created by non-Sharc tools (plain SQLite) won't have GUIDs — assigned lazily on first Sharc access

### Tier 4 — File Identity (GUID, Header-Level)

**What:** A persistent, unique identifier for the database file itself.

**Representation:** Two options (choose one at implementation time):

**Option A — ApplicationId + UserVersion (SQLite-native, 64-bit):**
The SQLite header already has `ApplicationId` (4 bytes at offset 68) and `UserVersion` (4 bytes at offset 60). Together they form a 64-bit identifier. Sharc could set `ApplicationId = 0x53484152` ("SHAR") as a Sharc sentinel and use `UserVersion` as a 32-bit random ID. Limitation: only 32 bits of uniqueness — not globally unique for large-scale distributed systems.

**Option B — Metadata table (128-bit GUID):**
Store a `_sharc_file_guid` in the Sharc metadata table (same table as table GUIDs). Full 128-bit GUID with true global uniqueness. Requires one extra row read on database open.

**Option C — Reserved header bytes (128-bit, SQLite-compatible):**
SQLite header bytes 72–91 are reserved and defined as zero. Sharc could write a 16-byte GUID at offset 72 and set `ApplicationId = 0x53484152` as a signal that the reserved bytes contain a Sharc file GUID. Advantage: no table lookup required — read directly from page 1. Risk: future SQLite versions might assign meaning to these bytes.

**Recommendation:** Option B (metadata table). It's the safest, most extensible, and doesn't risk SQLite header compatibility. The one-time read cost on database open is negligible.

**When to use:** Any scenario where different `.db` files need to be distinguished — sharding, replication, agent ledger sync, cross-file references.

**Where it will apply:**
- Shard coordinator — maps `FileGuid` to physical file path or network location
- Agent trust layer — ledger entries reference `FileGuid` so agents can sync across databases
- Cross-file dedup — when doing UNION across two Sharc files, `(FileGuid, TableGuid, RowId)` provides exact identity without content hashing

---

## Decision Matrix: When to Use Which Tier

| Scenario | Tier | Identity Tuple | Cost |
|:---------|:-----|:---------------|:-----|
| Point lookup by rowid | T0 | `(RowId)` | Free |
| Delete/Update by rowid | T0 | `(RowId)` | Free |
| Cursor bookmarking within a read | T1 | `(Page, Cell)` | Free |
| UNION/INTERSECT/EXCEPT dedup | T2 | `Fingerprint128` | O(bytes) |
| GROUP BY text pooling | T2 | `Fingerprint128` (column) | O(bytes) |
| Cross-file row reference | T3+T4 | `(FileGuid, TableGuid, RowId)` | Cold |
| Agent ledger cross-reference | T3+T4 | `(FileGuid, TableGuid, RowId)` | Cold |
| Distributed shard routing | T4 | `(FileGuid)` | Cold |

---

## Current Usage Audit

### Correct Tier Usage (No Change Needed)

| Component | Current Tier | Rationale |
|:----------|:-------------|:----------|
| `SharcDataReader.Seek(rowId)` | T0 | Single-table point lookup — rowid is sufficient |
| `SharcWriter.Delete/Update` | T0 | Mutation by rowid — inherent identity |
| `PreparedReader.CreateReader()` | T0 | Cursor reset, no identity needed beyond table root |
| `GetRowFingerprint()` | T2 | Cross-cursor set dedup — **must be T2**, no cheaper option |
| `GetColumnFingerprint()` | T2 | GROUP BY text pooling — **must be T2**, content-addressed |
| `IndexSet` | T2 | INTERSECT/EXCEPT pre-scan — fingerprint-keyed by design |

### Not Yet Implemented

| Component | Needed Tier | Status |
|:----------|:------------|:-------|
| Table GUID | T3 | Not implemented — needed for distributed sharding |
| File GUID | T4 | Not implemented — needed for cross-file agent sync |
| Cross-file UNION dedup | T3+T4+T0 | Not implemented — falls back to T2 value hashing today |

---

## Fingerprint128 / Fnv1aHasher: Keep or Replace?

**Keep.** Fingerprint128 is the right tool for its job. The question is not "should we replace it?" but "should we use it where we don't need it?"

Current analysis shows **zero misuse** — every Fingerprint128 call site genuinely needs content-addressed identity (cross-cursor dedup). There are no cases where a cheaper tier would suffice.

The only expansion is **upward** — adding T3 (table GUID) and T4 (file GUID) for distributed scenarios where Fingerprint128 can't help because identity must be stable across mutations and file copies.

### FNV-1a Performance Characteristics

| Operation | Cost | Notes |
|:----------|:-----|:------|
| `Append(span)` | ~1 cycle/byte | Two multiplies per byte (dual-lane) |
| `AppendString(s)` | ~1 cycle/byte + UTF-8 encode | stackalloc for ≤128 chars |
| `AppendLong(v)` | ~16 cycles | 8 iterations, 2 multiplies each |
| `AddTypeTag(col, type)` | ~2 cycles | Shift + XOR |
| Total per row (5 cols, 100B) | ~250 cycles | Dominated by byte iteration |
| Managed allocation | **0 B** | `ref struct` — entirely stack-resident |

At 250 cycles per row on a 3 GHz CPU, hashing adds ~83 ns per row. For set operations processing millions of rows, this is negligible compared to B-tree traversal (~800 ns/row) and I/O.

---

## Distributed Identity Architecture (Future)

When agents process distributed ledgers across sharded files:

```
┌──────────────────────────────────────────────────┐
│  Agent A (Local)           Agent B (Remote)       │
│  ┌────────────────┐       ┌────────────────┐     │
│  │ shard-01.db    │       │ shard-02.db    │     │
│  │ FileGuid: 0x1A │       │ FileGuid: 0x2B │     │
│  │                │       │                │     │
│  │ users (T.Guid  │       │ users (T.Guid  │     │
│  │  = 0xAA)       │       │  = 0xBB)       │     │
│  │  rowid 42      │       │  rowid 42      │     │
│  └────────────────┘       └────────────────┘     │
│                                                    │
│  Row ref: (0x1A, 0xAA, 42)  ≠  (0x2B, 0xBB, 42) │
│  Globally unique without content hashing           │
└──────────────────────────────────────────────────┘
```

**Ledger entry format (extended):**

```
SequenceNumber | Timestamp | AgentId | FileGuid | TableGuid | RowId | Payload | ...
```

The `(FileGuid, TableGuid, RowId)` triple in each ledger entry enables:
- **Conflict detection** — same row modified by two agents on different shards
- **Merge resolution** — deterministic ordering by `(FileGuid, SequenceNumber)`
- **Reference integrity** — agent B can resolve agent A's row references without content hashing

---

## Implementation Roadmap

### Phase 1: Metadata Table Foundation
- Create `_sharc_meta` table: `Key TEXT PRIMARY KEY, Value BLOB`
- Store `file_guid` as first entry (generated on first Sharc write if absent)
- Expose via `SharcDatabaseInfo.FileGuid` (Guid?)

### Phase 2: Table GUIDs
- Add `table:{name}:guid` entries to `_sharc_meta` on table creation
- Lazy assignment for tables created by plain SQLite
- Expose via `TableInfo.TableGuid` (Guid?)

### Phase 3: Cross-File Identity API
- `GlobalRowId` struct: `(Guid FileGuid, Guid TableGuid, long RowId)` — 40 bytes
- `SharcDataReader.GetGlobalRowId()` for cross-file references
- Ledger entries gain optional `FileGuid` + `TableGuid` columns

### Phase 4: Distributed Shard Coordination
- `ShardRegistry` — maps `FileGuid` → connection info
- Cross-shard UNION using `GlobalRowId` dedup instead of Fingerprint128
- Agent sync protocol using `GlobalRowId` references in ledger payloads

---

## Policy Rules

1. **Always use the cheapest identity tier that satisfies the requirement.** Don't hash when rowid suffices. Don't allocate GUIDs when position suffices.

2. **Fingerprint128 is reserved for cross-cursor value dedup.** Never compute a fingerprint when you already have a rowid and know both rows are from the same table.

3. **Table and File GUIDs are immutable once assigned.** Vacuuming, compacting, and copying preserve them. Only `CREATE TABLE` and first-Sharc-access assign new GUIDs.

4. **Distributed identity uses `(FileGuid, TableGuid, RowId)`, not content hashing.** Content hashing (Fingerprint128) answers "are these values the same?" — structural identity answers "is this the same row?"

5. **All identity mechanisms must be zero-alloc on hot paths.** GUIDs are resolved once at schema load (cold). Fingerprint128 is already zero-alloc (ref struct). Rowid is free (cursor state). No new identity mechanism may allocate on the per-row path.
