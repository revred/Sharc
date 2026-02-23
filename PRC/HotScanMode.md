# Hot Scan Mode — Pre-Resolved Branchless Execution

## Overview

Hot Scan Mode is an optimization in `SharcDataReader.Read()` that eliminates **all runtime branching** from the scan hot loop for CACHED and JIT execution tiers. Instead of checking 7+ conditions per `Read()` call, the exact execution path is resolved once (in the constructor or `ResetForReuse`) and encoded as a `ScanMode` enum. At runtime, `Read()` performs a single switch dispatch — compiled by the JIT to a jump table — and lands directly in a specialized, branch-free scan method.

## Problem: Branch Cascade in Read()

Before Hot Scan Mode, every `Read()` call traversed this cascade:

```
Read()
├── ObjectDisposedException.ThrowIf(_disposed, this)     // Branch 1
├── if (_dedupUnderlying != null) ...                     // Branch 2 — always false for CACHED/JIT
├── if (_concatFirst != null) ...                         // Branch 3 — always false
├── if (_queryValueRows != null) ...                      // Branch 4 — always false
├── if (_queryValueList != null) ...                      // Branch 5 — always false
├── if (_queryValueEnumerator != null) ...                // Branch 6 — always false
├── if (_btreeCachedCursor != null) → ScanTyped(...)      // Branch 7 — type dispatch
│   └── ProcessRow()
│       ├── if (_filterNode != null) ...                  // Branch 8 — always true for filtered
│       ├── if (_concreteFilterNode != null) ...           // Branch 9 — always true
│       └── DecodeCurrentRow()
│           └── if (_filterNode != null) ...              // Branch 10 — always true
```

For a filtered scan over 2,500 rows (~1,000 matches, ~2.5 rows scanned per match):
- **Read() called ~1,000 times** → 7 dead branches × 1,000 = **7,000 wasted branches**
- **ProcessRow called ~2,500 times** → 2 branches × 2,500 = **5,000 wasted branches**
- **DecodeCurrentRow called ~1,000 times** → 1 branch × 1,000 = **1,000 wasted branches**

Total: **~13,000 predicted-not-taken branches per query** consuming instruction cache, branch predictor slots, and decode bandwidth.

## Solution: ScanMode Enum + Specialized Methods

### The Enum

```csharp
private enum ScanMode : byte
{
    Default,           // Composite/dedup/concat/materialized/interface — existing cascade
    FilteredCached,    // Concrete FilterNode + BTreeCursor<CachedPageSource>
    UnfilteredCached,  // No filter + BTreeCursor<CachedPageSource>
    FilteredMemory,    // Concrete FilterNode + BTreeCursor<MemoryPageSource>
    UnfilteredMemory,  // No filter + BTreeCursor<MemoryPageSource>
    Disposed,          // Reader has been disposed — Read() throws
}
```

### Resolution Points

| Event | Action | Why |
|---|---|---|
| Constructor | `_scanMode = ResolveScanMode()` | Initial mode based on cursor type + filter |
| `ResetForReuse(filterNode)` | `_scanMode = ResolveScanMode()` | Filter may change for parameterized queries |
| `Dispose()` | `_scanMode = ScanMode.Disposed` | Prevents use-after-dispose |
| `DisposeForReal()` | `_scanMode = ScanMode.Default` | Allows cleanup to proceed |

### Resolution Logic

```csharp
private ScanMode ResolveScanMode()
{
    bool hasConcreteFilter = _concreteFilterNode != null;
    bool isUnfiltered = _filterNode == null && (_filters == null || _filters.Length == 0);

    if (_btreeCachedCursor != null)
    {
        if (hasConcreteFilter) return ScanMode.FilteredCached;
        if (isUnfiltered) return ScanMode.UnfilteredCached;
    }
    else if (_btreeMemoryCursor != null)
    {
        if (hasConcreteFilter) return ScanMode.FilteredMemory;
        if (isUnfiltered) return ScanMode.UnfilteredMemory;
    }

    return ScanMode.Default;
}
```

Non-matching combinations (e.g., IFilterNode interface without concrete FilterNode, legacy ResolvedFilter[], non-BTreeCursor cursors like IndexSeekCursor) fall through to `Default`, which uses the original branch cascade in `ReadDefault()`.

### The Dispatch

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool Read()
{
    return _scanMode switch
    {
        ScanMode.FilteredCached => ScanFilteredCached(),
        ScanMode.UnfilteredCached => ScanUnfilteredCached(),
        ScanMode.FilteredMemory => ScanFilteredMemory(),
        ScanMode.UnfilteredMemory => ScanUnfilteredMemory(),
        ScanMode.Disposed => throw new ObjectDisposedException(...),
        _ => ReadDefault(),
    };
}
```

The JIT compiles the 6-case switch on a `byte` enum to a **jump table**: a single indexed branch that goes directly to the target method. No sequential if-else chain, no branch prediction misses.

### Disposed State Unification

The `_disposed` boolean field was eliminated entirely. The `ScanMode.Disposed` enum value serves as the single source of truth for lifecycle state. This saves one field read + branch check per Read() call since the disposed check is folded into the same jump table lookup.

## Post-Jump: The "Closure Pack"

What matters most is **what happens after the jump**. The specialized scan methods hoist all hot references into local variables, enabling the JIT to keep them in CPU registers for the entire scan loop:

```csharp
private bool ScanFilteredCached()
{
    // "Closure pack" — all hot references hoisted to locals → CPU registers
    var cursor = _btreeCachedCursor!;      // BTreeCursor<CachedPageSource> — sealed, direct calls
    var decoder = _recordDecoder!;          // IRecordDecoder
    var st = _serialTypes!;                // long[] — shared serial type buffer (no separate filter copy)
    var filter = _concreteFilterNode!;      // FilterNode — sealed, direct Evaluate()
    int physColCount = _physicalColumnCount;

    while (cursor.MoveNext())              // Direct call (sealed class, no vtable)
    {
        var payload = cursor.Payload;       // Direct property access
        int colCount = decoder.ReadSerialTypes(payload, st, out int bodyOffset);
        int stCount = Math.Min(colCount, st.Length);

        if (filter.Evaluate(payload, st.AsSpan(0, stCount), bodyOffset, cursor.RowId))
        {
            // Inline decode — serial types already in _serialTypes, skip Array.Copy
            _currentBodyOffset = bodyOffset;
            int cols = Math.Min(physColCount, st.Length);
            decoder.ComputeColumnOffsets(
                st.AsSpan(0, cols), cols, bodyOffset,
                _columnOffsets.AsSpan(0, cols));
            _decodedGeneration++;
            _lazyMode = true;
            _currentRow = _reusableBuffer;
            return true;
        }
    }

    _currentRow = null;
    _lazyMode = false;
    return false;
}
```

### Why This Is Fast

1. **Zero null checks**: No `_filterNode != null`, no `_concreteFilterNode != null`, no `_filters != null`, no mode checks. All known at resolve time.

2. **Register allocation**: `cursor`, `decoder`, `filterST`, `filter` are locals, not field accesses. The JIT keeps them in registers for the entire while loop. Field reads require loading from `this` pointer + offset on each access; locals can stay in `rax`, `rbx`, `rcx`, `rdx`.

3. **Devirtualized calls**: `cursor.MoveNext()` and `filter.Evaluate()` are direct calls because `BTreeCursor<T>` and `FilterNode` are sealed classes. No vtable lookup, no interface dispatch, no GDV guard check.

4. **Deferred field writes**: `_filterColCount` and `_filterBodyOffset` are written only when a row matches the filter (~40% of rows). Non-matching rows (the common case in the tight loop) touch zero instance fields.

5. **Branch-free tight loop**: The while loop body has exactly one branch — the filter evaluation. Everything else is straight-line code.

## Unfiltered Hot Path

For queries without WHERE clauses (e.g., `SELECT * FROM table`), the unfiltered variant is even simpler:

```csharp
private bool ScanUnfilteredCached()
{
    if (!_btreeCachedCursor!.MoveNext())
    {
        _currentRow = null;
        _lazyMode = false;
        return false;
    }
    DecodeCurrentRow();
    return true;
}
```

No loop needed — every row matches, so we advance exactly one position per Read() call.

## Fallback: ReadDefault()

The `Default` scan mode preserves the original branch cascade for composite readers (UNION ALL concat, UNION/INTERSECT/EXCEPT dedup, materialized QueryValue rows) and non-standard cursors (IndexSeekCursor, WithoutRowIdCursorAdapter). These paths are cold (not performance-critical) and benefit from the existing code's flexibility.

## Expected Impact

| Optimization | Eliminated per Read() | Eliminated per row |
|---|---|---|
| Mode null checks (dedup/concat/materialized) | 5 branches | — |
| Cursor type dispatch | 1 branch | — |
| ObjectDisposedException check | 1 branch (folded into jump table) | — |
| `_filterNode != null` | — | 1 branch |
| `_concreteFilterNode != null` | — | 1 branch |
| Field→local hoisting | — | ~4 memory loads |

For a 2,500-row filtered scan with ~1,000 matches:
- **7,000 fewer branches** from Read() cascade
- **5,000 fewer branches** from ProcessRow inlining
- **~10,000 fewer memory loads** from field→local hoisting

Combined with Phase 3 cursor devirtualization (which eliminated ~10,000 interface dispatches per query), the total branch+dispatch reduction is ~32,000 operations per query.
