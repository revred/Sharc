# B-Tree Fingerprint Set Operations

## Overview

Sharc's compound query pipeline (UNION / INTERSECT / EXCEPT) uses **raw-byte fingerprinting** to perform set deduplication without materializing strings. Instead of decoding every row into managed `QueryValue[]` arrays (which allocates ~500 KB of strings for 5,000 rows), the pipeline hashes raw B-tree cursor payload bytes using FNV-1a 64-bit to produce a compact fingerprint per row.

Only rows that **survive** the set operation are decoded — and only when the caller actually reads a column (lazy materialization).

## Architecture

### Previous Flow (~1.2-1.6 MB allocation)

```
left reader  → materialize ALL QueryValue[] (incl. GetString()) → HashSet dedup → result list
right reader → materialize ALL QueryValue[] (incl. GetString()) → HashSet dedup
```

Every row on both sides is fully decoded into managed objects, including UTF-8 string allocation for TEXT columns. The HashSet uses a composite equality comparer over the `QueryValue[]` arrays.

### Fingerprint Flow (~186-317 KB allocation)

```
right reader → hash raw cursor bytes → HashSet<ulong> (fingerprints only, ~20 KB)
left reader  → hash raw cursor bytes → check fingerprint set → streaming dedup reader
caller       → GetInt64() / GetString() → lazy decode from cursor (zero pre-alloc)
```

**Key insight**: `IBTreeCursor.Payload` returns the raw record bytes (header + body) directly from page spans. For overflow records, the cursor assembles the full payload transparently. These bytes are deterministic for the same logical values — SQLite's record format always picks the smallest serial type encoding.

## How Fingerprinting Works

### FNV-1a 64-bit Hash

Each row's fingerprint is computed by hashing its **projected column bytes** (not the entire record):

1. For each projected column, hash the **serial type** (8 bytes) followed by the **column body bytes**
2. Serial types encode both the data type and size, ensuring type-safe comparison
3. For INTEGER PRIMARY KEY (rowid alias), hash `cursor.RowId` instead of the NULL record bytes

The hash function is FNV-1a 64-bit, implemented as a zero-allocation `ref struct`:

```
offset_basis = 14695981039346656037
prime        = 1099511628211

for each byte b:
    hash ^= b
    hash *= prime
```

### Collision Probability

For N = 5,000 rows and a 64-bit hash space:

```
P(collision) = N^2 / (2 * 2^64) = 25,000,000 / 36,893,488,147,419,103,232 ~ 6.8 x 10^-13
```

This is approximately **0.000000000068%** — far below the 0.0001% threshold. Even at 1 million rows, the collision probability is ~2.7 x 10^-8 (0.0000027%).

### Why Projection Matters

A query like `SELECT id, name, dept FROM users` projects 3 of 6 physical columns. Hashing the full payload would include `age`, `score`, `active` — causing false mismatches between rows that differ only in non-projected columns. The `_projection` array (`[0, 1, 3]`) tells the fingerprint computation exactly which physical columns to include.

### Why Rowid Alias Matters

SQLite stores `NULL` in the record for INTEGER PRIMARY KEY columns; the real value lives in `cursor.RowId`. Without special handling, two rows with different IDs but identical other columns would produce the same fingerprint. The implementation detects the rowid alias ordinal and hashes the 8-byte rowid instead.

## Set Operation Modes

The `SetDedupMode` enum controls fingerprint-based dedup behavior:

| Mode | Behavior |
|:-----|:---------|
| `Union` | Concat both readers, emit row if `seen.Add(fp)` succeeds (first occurrence only) |
| `Intersect` | Build right-side fingerprint set, emit left row if `rightSet.Contains(fp) && seen.Add(fp)` |
| `Except` | Build right-side fingerprint set, emit left row if `!rightSet.Contains(fp) && seen.Add(fp)` |

For INTERSECT and EXCEPT, the right-side reader is fully scanned first to build the `HashSet<ulong>` fingerprint set (~20 KB for 2,500 rows). Then the left-side reader streams through a dedup filter. The right reader is disposed immediately after fingerprint collection.

## Streaming Pipeline Integration

The fingerprint path integrates with existing post-processing:

```
fingerprint dedup reader
    → StreamingTopNProcessor (ORDER BY + LIMIT, heap-based)
    → or: Materialize + ApplyOrderBy + ApplyLimitOffset (complex cases)
```

For simple `UNION/INTERSECT/EXCEPT` without ORDER BY, the dedup reader is returned directly to the caller as a streaming `SharcDataReader`.

## Benchmark Results

Dataset: 2 tables, 2,500 rows each, 500-row overlap. Schema: `id, name, age, dept, score, active`.

| Operation | Before (Materialized) | After (Fingerprint) | Reduction |
|:----------|----------------------:|--------------------:|----------:|
| UNION (dedup, 500 overlap) | ~1,630 KB | ~317 KB | **5.1x** |
| INTERSECT (500 common) | ~1,210 KB | ~186 KB | **6.5x** |
| EXCEPT (left - right) | ~1,500 KB | ~302 KB | **5.0x** |
| UNION ALL (no dedup) | ~405 KB | ~405 KB | (streaming, no change) |
| UNION ALL + ORDER BY + LIMIT 50 | ~32 KB | ~32 KB | (TopN heap, no change) |

Benchmarks read `GetInt64(0)` only (no string columns), demonstrating the lazy-decode benefit: strings are never allocated unless the caller requests them.

## Files Modified

| File | Changes |
|:-----|:--------|
| `src/Sharc/SharcDataReader.cs` | `GetRowFingerprint()`, `GetCursorRowFingerprint()`, `ComputeFingerprint()`, `Fnv1aHasher` ref struct, `SetDedupMode` enum, dedup constructor + Read() + accessor delegation |
| `src/Sharc/Query/CompoundQueryExecutor.cs` | `ExecuteIndexSetOp()`, `BuildIndexSet()`, wiring in `Execute()` |

No new files created. `StreamingSetOpProcessor` remains as the fallback path for complex compounds and Cote references.

## Edge Cases

1. **Overflow pages**: `BTreeCursor.Payload` assembles overflow pages into a contiguous buffer. Fingerprinting works transparently across page boundaries.

2. **NULL values**: Serial type 0 encodes NULL with zero body bytes. The serial type alone distinguishes NULL from zero-length TEXT/BLOB (serial types 12/13).

3. **Mixed types**: SQLite's type affinity means the same column can hold different storage classes across rows. The serial type in the fingerprint captures the actual storage class, preventing cross-type false matches.

4. **Non-cursor readers**: When the underlying reader is in materialized mode (Cote results, complex compounds), the fingerprint falls back to `System.HashCode` over the `QueryValue[]` array. This is slower but correct.
