# BakedFilter: The JIT-Compiled Predicate Engine

**BakedFilter** (internally `FilterStar`) is Sharc's high-performance query execution engine. Unlike traditional interpreters that walk an expression tree for every row, BakedFilter compiles your query predicates into **raw MSIL delegates** at runtime using `System.Linq.Expressions`.

## 🚀 Architecture

The engine operates in three phases:

1.  **Analysis**: The `FilterTreeCompiler` scans the expression tree to identify all referenced columns and standardizes types.
2.  **JIT Compilation**: `JitPredicateBuilder` constructs a single `Expression<BakedDelegate>` lambda that represents the entire filter logic.
3.  **Execution**: The delegate is compiled and cached. For each row, the engine invokes this delegate, passing direct pointers to the memory-mapped record data.

### The `BakedDelegate` Signature

The generated code has this exact signature, ensuring zero-allocation execution:

```csharp
public delegate bool BakedDelegate(
    ReadOnlySpan<byte> payload,      // Raw record bytes
    ReadOnlySpan<long> serialTypes,  // Column type headers
    ReadOnlySpan<int> offsets,       // Pre-calculated byte offsets
    long rowId                       // The row's B-tree key
);
```

## ⚡ Performance Features

### 1. Offset Hoisting
Before the filter executes, Sharc scans the record header *once* to calculate the byte offset of every column. These offsets are passed to the delegate, allowing O(1) access to column data without re-parsing the header for each predicate.

### 2. Type Specialization
The JIT compiler emits type-specific comparison code.
*   **Integers**: Compares raw `Int64` values.
*   **Floats**: Compares raw `Double` values.
*   **Strings**: Uses SIMD-accelerated `ReadOnlySpan<byte>` comparisons (UTF-8).
*   **Sets**: `IN (...)` clauses are compiled into `HashSet<T>.Contains` lookups.

### 3. Null Semantics (SQLite Compatible)
The engine strictly enforces SQLite's null handling rules:
*   `NULL = NULL` is False.
*   `NULL != NULL` is False.
*   Only `IS NULL` returns True for null values.

## 🛠️ Supported Operations

The `JitPredicateBuilder` supports a rich set of operations:

### Comparisons
*   `=`, `!=`, `<`, `<=`, `>`, `>=`
*   **Optimization**: Directly compares primitive values.

### String Operations
*   `STARTS_WITH`, `ENDS_WITH`, `CONTAINS`
*   **Optimization**: Operates on UTF-8 bytes without string allocation.

### Logical Operators
*   `AND`, `OR`, `NOT`
*   **Optimization**: Short-circuit evaluation logic is emitted directly into the IL.

### Range & Set Operations
*   `BETWEEN 10 AND 20`
*   `IN (1, 2, 3)` / `NOT IN (...)`

### RowID Alias
*   Filters on the `rowid` (or `_rowid_`) are optimized to check the B-tree key directly, avoiding payload access entirely when possible.

## 🛡️ Defensibility

**Why not native C?**
Sharc achieves "native C" speeds by leveraging the **.NET JIT Compiler**. By emitting efficient IL that accesses `ReadOnlySpan<byte>`, we get:
1.  **Safety**: Bounds checking and memory safety are managed by the runtime.
2.  **Portability**: Runs anywhere .NET runs (Windows, Linux, macOS, WASM).
3.  **Speed**: The JIT optimizes for the specific CPU instruction set (AVX/SSE) of the host machine.
