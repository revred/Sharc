# Deep Dive: From Text to Intent

**"Parsing is the art of turning chaos into structure without allocating memory."**

Sharc's query engine, **Sharq**, is built on a radical premise: query parsing should generate zero garbage. To achieve this, we bypassed traditional tools like ANTLR or Sprague and built a hand-tuned recursive descent parser purely on `ref struct` and `ReadOnlySpan<char>`.

Crucially, we leverage the **same foundation that powers the modern .NET Regular Expression engine**: the `System.Buffers.SearchValues<T>` type introduced in .NET 8.

## 1. The Foundation: `SearchValues<char>`

In .NET 7 and earlier, searching for "any of these characters" (e.g., finding the end of an identifier) required either a slow loop or a lookup table.

In .NET 8, the Regex engine was rewritten to use `SearchValues<T>`. This type analyzes the set of characters you want to find and **JIT-compiles a vectorized SIMD strategy** relative to your specific CPU (AVX2, AVX-512, NEON, etc.).

### How Sharq Uses It

Instead of a standard `while` loop to scan identifiers, `SharqTokenizer` uses:

```csharp
private static readonly SearchValues<char> s_identChars = 
    SearchValues.Create("_0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

// ... inside ScanIdentifier ...
int end = remaining.IndexOfAnyExcept(s_identChars);
```

This single line of code:
1.  Loads 16-32 characters of the SQL string into strict SIMD registers.
2.  Compares them against the allowed set in parallel.
3.  Returns the index of the first invalid character (e.g., a space or comma).

**Result**: We scan identifiers at RAM bandwidth speeds (GB/s), completely bypassing the overhead of character-by-character checks.

## 2. Zero-Allocation Tokenization

Traditional parsers allocate a `List<Token>` or `Token[]`. For a large SQL query, this creates hundreds of small objects that pressure the GC.

**SharqTokenizer** is a `ref struct`. It does not allocate. It holds a definition of the `ReadOnlySpan<char>` and yields `SharqToken` structs on demand.

```csharp
internal struct SharqToken
{
    public SharqTokenKind Kind;
    public int Start;  // Index into the original span
    public int Length; // Slice length
}
```

Because the token only stores integer offsets, the "Text" of the token remains resident in the original query string. We never call `new String()` until the very last moment (e.g., when looking up a column name in the schema).

## 3. Recursive Descent "Intent"

The `SharqParser` (also a `ref struct`) consumes these tokens to build the **Abstract Syntax Tree (AST)**. This AST represents the *user's intent*.

### The "Star" System
All expressions in Sharq are represented by the `SharqStar` hierarchy:
*   `LiteralStar` (integers, strings)
*   `ColumnRefStar` (identifiers)
*   `BinaryStar` (operators)
*   `ArrowStar` (graph traversal)

The parser uses **Precedence Climbing** to handle operator precedence (AND before OR, * before +) without explicit recursion limits.

```csharp
private SharqStar ParseExpr() => ParseOr();

private SharqStar ParseOr() {
    var left = ParseAnd();
    while (Match(Or)) { ... }
}
```

## 4. From Intent to Execution (The Hard Part)

Once we have the AST (`SelectStatement`), we must execute it. This is where Sharc's unique architecture shines.

### A. Binder (Semantic Analysis)
The `SharqBinder` walks the AST and matches `ColumnRefStar` nodes to actual `Shop.Core.Schema.ColumnInfo` objects. It validates types and resolves aliases.

### B. Filter Compilation (The "Baked" Layer)
The `WHERE` clause is not interpreted. It is passed to the **BakedFilter Compiler**.
1.  The AST is converted to `System.Linq.Expressions`.
2.  `FilterStarCompiler` emits a dynamic method.
3.  **Result**: An `Expression<BakedDelegate>` that executes the logic via direct memory access.

### C. Graph Rewriting
For `ArrowStar` nodes (`a |> b`), the engine rewrites the query into a **Graph Scan**.
*   `node |> edge` becomes a lookup in the `_sharc_edges` table.
*   The optimizer decides whether to start from the source node (forward scan) or target node (reverse scan) based on index statistics.

## Summary

| Layer | Technology | "Secret Sauce" |
| :--- | :--- | :--- |
| **Lexer** | `SharqTokenizer` | **SearchValues<char>** (SIMD Regex engine) |
| **Parser** | `SharqParser` | **Ref Structs** (Zero Allocation) |
| **AST** | `SharqStar` | **Precedence Climbing** |
| **Runtime** | `BakedFilter` | **JIT Compilation** (IL Generation) |

By aligning our lexer with the .NET runtime's own vectorized primitives, Sharq achieves a parsing throughput that rivals unmanaged C++ parsers.
