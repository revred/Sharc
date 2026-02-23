# BakedFilter: The Closure-Composed Predicate Engine

**BakedFilter** (internally `FilterStar`) is Sharc's high-performance query execution engine. Unlike traditional interpreters that walk a filter tree for every row, BakedFilter compiles your query predicates into **closure-composed delegates** at build time — no `System.Linq.Expressions`, no reflection, fully AOT-safe.

## Architecture

The engine operates in three phases:

1.  **Analysis**: The `FilterTreeCompiler` scans the filter tree to identify all referenced columns and standardizes types.
2.  **Delegate Composition**: `JitPredicateBuilder` constructs a single `BakedDelegate` by composing closures — one per predicate — chained with short-circuit AND/OR/NOT logic.
3.  **Execution**: The delegate is cached. For each row, the engine invokes this delegate, passing direct pointers to the memory-mapped record data.

### The `BakedDelegate` Signature

The composed delegate has this exact signature, ensuring zero-allocation execution:

```csharp
public delegate bool BakedDelegate(
    ReadOnlySpan<byte> payload,      // Raw record bytes
    ReadOnlySpan<long> serialTypes,  // Column type headers
    ReadOnlySpan<int> offsets,       // Pre-calculated byte offsets
    long rowId                       // The row's B-tree key
);
```

## Performance Features

### 1. Offset Hoisting
Before the filter executes, Sharc scans the record header *once* to calculate the byte offset of every column. These offsets are passed to the delegate, allowing O(1) access to column data without re-parsing the header for each predicate.

### 2. Type Specialization
The predicate builder emits type-specific comparison code via closures:

*   **Integers**: Compares raw `Int64` values with cross-type fallback to `Double`.
*   **Floats**: Compares raw `Double` values with cross-type fallback from `Int64`.
*   **Strings**: Uses SIMD-accelerated `ReadOnlySpan<byte>` comparisons (UTF-8).
*   **Sets**: `IN (...)` clauses use pre-built `HashSet<T>.Contains` lookups.

### 3. Null Semantics (SQLite Compatible)
The engine strictly enforces SQLite's null handling rules:
*   `NULL = NULL` is False.
*   `NULL != NULL` is False.
*   Only `IS NULL` returns True for null values.

## Supported Operations

The `JitPredicateBuilder` supports a rich set of operations:

### Comparisons
*   `=`, `!=`, `<`, `<=`, `>`, `>=`
*   **Optimization**: Directly compares primitive values on raw bytes.

### String Operations
*   `STARTS_WITH`, `ENDS_WITH`, `CONTAINS`
*   **Optimization**: Operates on UTF-8 bytes without string allocation.

### Logical Operators
*   `AND`, `OR`, `NOT`
*   **Optimization**: Short-circuit evaluation via composed delegate chains.

### Range & Set Operations
*   `BETWEEN 10 AND 20`
*   `IN (1, 2, 3)` / `NOT IN (...)`

### RowID Alias
*   Filters on the `rowid` (or `_rowid_`) are optimized to check the B-tree key directly, avoiding payload access entirely when possible.

## Defensibility

**Why closures instead of expression trees?**
Sharc achieves near-native speeds by composing simple delegates that the **.NET JIT Compiler** optimizes at runtime:

1.  **Zero bloat**: No `System.Linq.Expressions` (~400 KB) or `System.Reflection.Emit` dependency.
2.  **AOT-safe**: Works on WASM, NativeAOT, and all ahead-of-time compilation targets — no dynamic code generation required.
3.  **Safety**: Bounds checking and memory safety are managed by the runtime.
4.  **Portability**: Runs anywhere .NET runs (Windows, Linux, macOS, WASM).
5.  **Speed**: The JIT optimizes for the specific CPU instruction set (AVX/SSE) of the host machine.
