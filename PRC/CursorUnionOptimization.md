# Cursor Union Optimization — Decimate BTreeCursor via Polymorphic Projection

**Status**: Approved
**Constraint**: Zero new dependencies — no `System.Runtime.CompilerServices`, no `[InlineArray]`

## Problem

PointLookup: **430 ns, 680 B** (95x faster than SQLite). Allocation breakdown:

| Object | Size | % |
| --- | --- | --- |
| `SharcDataReader` | 288 B | 42% |
| `BTreeCursor<T>` | 144 B | 21% |
| `CursorStackFrame[8]` | 248 B | **37%** |

Cursor total: **392 B** (144 B object + 248 B heap array).

## The Union Principle

`BTreePageHeader` (16 B) has 6 fields. The cursor only reads 2–6 B depending on context. Storing the full struct everywhere wastes 10–14 B per instance. The fix: **store context-specific projections** (union members), not the universal representation.

```
BTreePageHeader (16 B — "universal" representation)
┌─────────────────┬──────────────┬────────────────┬─────────────────┬──────────────────┬───────────────┐
│ PageType (1 B)  │ FreeBlock    │ CellCount (2B) │ ContentOff (2B) │ FragBytes (1 B)  │ RightChild    │
│                 │ Offset (2 B) │                │                 │                  │ (4 B)         │
└─────────────────┴──────────────┴────────────────┴─────────────────┴──────────────────┴───────────────┘

Projection 1 — "Leaf View" (cursor's current position):
┌────────────────┐
│ CellCount (2B) │  ← Only field read from _currentHeader on leaf pages
└────────────────┘

Projection 2 — "Interior Stack View" (popped frames in MoveToNextLeaf):
┌────────────────┬───────────────┐
│ CellCount (2B) │ RightChild(4B)│  ← Only fields read after StackPop
└────────────────┴───────────────┘
→ Re-derived from cached page on pop (~70 cycles, negligible)

Projection 3 — "Write View" (BTreeMutator — unchanged):
Full BTreePageHeader — all 6 fields needed for splits, defrag, freelist
```

### Field-Usage Audit

| BTreePageHeader Field | Cursor on leaf? | Stack pop? | Write engine? |
| --- | --- | --- | --- |
| `PageType` | Never | Never | Yes |
| `FirstFreeblockOffset` | **Never** | **Never** | Yes |
| `CellCount` | **Yes** | **Yes** | Yes |
| `CellContentOffset` | **Never** | **Never** | Yes |
| `FragmentedFreeBytes` | **Never** | **Never** | Yes |
| `RightChildPage` | Never (0 on leaf) | **Yes** | Yes |

### Derived Fields

| Field | Derivation | Cost |
| --- | --- | --- |
| `_currentHeaderOffset` (4 B) | `_currentLeafPage == 1 ? 100 : 0` | 1 branch, always-false predicted |
| `HeaderOffset` in stack frame (4 B) | `pageId == 1 ? 100 : 0` | Same |
| `GetCellPointer` HeaderSize | Leaf: constant `8`. Interior: constant `12`. | Compile-time |

## Implementation: Explicit-Layout Union Stack

### CursorStack — `StructLayout(Explicit)` union of 8 packed ulongs

No `[InlineArray]`, no `CompilerServices`. Uses `System.Runtime.InteropServices.StructLayout` with `LayoutKind.Explicit` to create a true C#-native union struct:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct CursorStack
{
    [FieldOffset(0)]  private ulong _f0;
    [FieldOffset(8)]  private ulong _f1;
    [FieldOffset(16)] private ulong _f2;
    [FieldOffset(24)] private ulong _f3;
    [FieldOffset(32)] private ulong _f4;
    [FieldOffset(40)] private ulong _f5;
    [FieldOffset(48)] private ulong _f6;
    [FieldOffset(56)] private ulong _f7;

    internal ulong this[int index]
    {
        get => index switch { 0 => _f0, 1 => _f1, ..., _ => 0 };
        set { switch (index) { case 0: _f0 = value; break; ... } }
    }
}
```

Each frame packs `PageId` (32 bits) + `CellIndex` (16 bits) into a single ulong:

```
┌──────────────────────────────────┬──────────────────┬──────────────────┐
│ PageId (bits 63..16, 32 used)    │ CellIndex (15..0)│ (16 spare bits)  │
└──────────────────────────────────┴──────────────────┴──────────────────┘

Pack:   ((ulong)pageId << 16) | (uint)(ushort)cellIndex
Unpack: pageId = (uint)(frame >> 16),  cellIndex = (int)(ushort)frame
```

**CellCount + RightChildPage re-derived** from cached page on pop. MoveToNextLeaf already calls `GetPage()` — re-parsing the 12-byte header costs ~70 cycles. PointLookup never pops.

### Leaf State → `_leafCellCount` (ushort)

Replace `_currentHeader` (16 B) + `_currentHeaderOffset` (4 B) with `_leafCellCount` (2 B). Inline cell pointer math: `page[(ho + 8 + cellIndex * 2)..]` where `ho` is derived.

### State Flags → 1 byte

3 bools (`_initialized`, `_exhausted`, `_disposed`) → single `_state` byte with bit constants.

### Cache Tag: `_cachedLeafPageNum` — Intentionally Retained

The lazy check `_currentLeafPage != _cachedLeafPageNum` costs 0 effective cycles (branch-predicted) and buys **self-healing correctness**. Any new code path that sets `_currentLeafPage` gets correct cache behavior automatically. Eliminating it saves 4 B but creates a silent-data-corruption risk on any future feature that repositions the cursor.

## Memory Layout

### Before: 392 B total (144 B object + 248 B heap)

```
BTreeCursor<T>: 144 B object
├── 5 refs × 8 B = 40 B  (_pageSource, _stack, _assembledPayload, _visitedOverflowPages, _writableSource)
├── ReadOnlyMemory<byte> = 16 B  (_cachedLeafMemory)
├── BTreePageHeader = 16 B  (_currentHeader — 14/16 B wasted)
├── 2 longs × 8 B = 16 B  (_rowId, _snapshotVersion)
├── 9 ints × 4 B = 36 B  (includes _currentHeaderOffset — derivable)
├── 3 bools + pad = 4 B
└── Object header = 16 B

CursorStackFrame[8]: 248 B heap object
├── Array header = 16 B
└── 8 × 28 B frames = 224 B  (each stores full BTreePageHeader — 10/16 B wasted)
```

### After: ~200 B total (single object, zero heap)

```
BTreeCursor<T>: ~200 B object
├── 5 refs × 8 B = 40 B  (_pageSource, _assembledPayload, _visitedOverflowPages, _writableSource, _stackOverflow)
├── ReadOnlyMemory<byte> = 16 B  (_cachedLeafMemory)
├── 2 longs × 8 B = 16 B  (_rowId, _snapshotVersion)
├── 8 ints × 4 B = 32 B  (_currentHeaderOffset GONE, _cachedLeafPageNum KEPT)
├── 1 ushort = 2 B  (_leafCellCount — was 16 B BTreePageHeader)
├── 1 byte = 1 B  (_state flags — was 3 bools)
├── padding ≈ 5 B
├── CursorStack [Explicit union] = 64 B  (was 248 B separate heap)
└── Object header = 16 B

Heap: 0 B
```

### Impact

| Metric | Before | After | Change |
| --- | --- | --- | --- |
| Cursor object | 144 B | ~200 B | +56 B (inline stack) |
| Heap array | 248 B | 0 B | **-248 B** |
| **Cursor total** | **392 B** | **~200 B** | **-192 B (49%)** |
| **PointLookup total** | **680 B** | **~488 B** | **-192 B (28%)** |

## Files to Modify

| File | Change |
| --- | --- |
| `src/Sharc.Core/BTree/BTreeTypes.cs` | Add `CursorStack` (LayoutKind.Explicit union) with pack/unpack statics |
| `src/Sharc.Core/BTree/BTreeCursor.cs` | Replace fields + all methods (Stack, Descent, Leaf, State) |

## What Does NOT Change

- `CursorStackFrame` — kept for `IndexBTreeCursor` and write engine
- `BTreePageHeader` — kept for write engine, schema reader
- `IndexBTreeCursor` — uses `Stack<CursorStackFrame>` (separate optimization)
- All public API — zero surface change
- `SharcDataReader` — no changes
- **Zero new dependencies** — only `System.Runtime.InteropServices` (already in BCL)

## Verification

1. `dotnet build` — zero errors
2. `dotnet test` — all 2,669 tests pass
3. PointLookup benchmark → 680 B → ~488 B
4. SequentialScan → no regression (re-parse ~1.5 μs out of 3 ms)
5. Seek correctness — exact match, near miss, exhaustion all covered by existing tests
