# BakedFilter: High-Performance Database Predicates

The **BakedFilter** is Sharc's "star weapon" for competing with native-C database engines. It transforms high-level C# filter expressions into optimized, zero-allocation machine code at runtime.

## 🏛️ Three-Tier Architecture

To balance correctness, readability, and raw speed, the engine uses three distinct execution tiers:

### Tier 1: Reference Interpreter
- **Goal**: Correctness & Debugging.
- **Tech**: Tree-based visitor pattern (`IFilterNode` implementations).
- **Behavior**: Slowest path, used as a fallback if JIT is unavailable or during development.

### Tier 2: JIT-Compiled (The "Baked" Path)
- **Goal**: Competitive Performance.
- **Tech**: `System.Linq.Expressions`.
- **Strategy**: 
    - **Offset Hoisting**: Scans the record header once per row. All predicates then access pre-calculated byte offsets.
    - **De-virtualization**: The entire filter tree is flattened into a single Lambda expression, eliminating polymorphic calls.
    - **Inlining**: Predicate logic is inlined directly into the filter loop.

### Tier 3: SIMD Block (Future)
- **Goal**: Maximum Throughput.
- **Tech**: `System.Runtime.Intrinsics`.
- **Strategy**: Processes a block of 8-16 rows simultaneously using AVX/SSE vectors for batch comparison.
- **Why**: SIMD & Block Evaluation is by far the most important path for maximum throughput.
---

## 🏎️ Strategy: Offset Hoisting

In a standard record scan, a filter `age > 30 AND score < 50` currently performs:
1. Filter 1: Scans header to find "age" offset -> Decodes body.
2. Filter 2: Scans header *again* to find "score" offset -> Decodes body.

**BakedFilter** optimizes this:
1. **Hoisting**: Before the loop, identify all needed column ordinals [1, 4].
2. **Single Pass**: For each row, a specialized `OffsetTracker` populates a tiny stack-allocated span of current offsets.
3. **Direct Access**: Filters read directly from `payload[offsets[i]]`.

## 🏎️ Performance Results (Tier 2)

The Tier 2 implementation delivers a **2.9x performance boost** over the interpreted engine and **surpasses SQLite's native VDBE** by roughly 25%.

| Metric | Interpreted | Baked (JIT) | vs SQLite VDB | Improvement |
| :--- | :---: | :---: | :---: | :---: |
| **Table Scan (5k rows)** | 1,444 μs | **496 μs** | 659 μs | **2.9x Faster** |
| **Allocations** | 1.07 MB | **1.3 KB** | ~0 | **99.9% Reduced** |

*Hardware: 11th Gen Intel Core i7-11800H @ 2.30GHz*

## 🛡️ Implementation Details

### Offset Hoisting
Before the record loop starts, `FilterTreeCompiler` identifies all referenced columns. `FilterNode` then performs a single pass over the record header per row, populating a stack-allocated buffer with byte offsets.

### JIT Compilation
Nested filters (e.g., `AND(OR(x, y), z)`) are flattened into a single `BakedDelegate`:
```csharp
public delegate bool BakedDelegate(
    ReadOnlySpan<byte> payload, 
    ReadOnlySpan<long> serialTypes, 
    ReadOnlySpan<int> offsets, 
    long rowId);
```

### Safety & Compatibility
- **NULL Semantics**: Compilations include guards that return `false` for NULL columns, matching SQLite's official behavior.
- **Short Records**: Handles records with "added columns" via bounds-checked access in JIT helper methods.
- **AOT Compatibility**: Uses explicit `MethodInfo` lookups to remain compatible with Ahead-of-Time compilation.

## 🛡️ Defensibility
By using `System.Linq.Expressions`, Sharc remains a **Pure C#** library with zero native dependencies, yet it achieves "native-level" speeds by handing execution control to the .NET JIT compiler, which specializes the filter code for the specific CPU architecture at runtime.
