# SharQL Parser Design

## Architecture

The SharQL parser is a zero-allocation recursive descent parser that converts query strings into an AST (Abstract Syntax Tree). It lives in `src/Sharc.Core/Query/SharQL/`.

### Memory Frugality

Both the tokenizer and parser are `ref struct` types — they live entirely on the stack:

- **Tokenizer** (`SharQlTokenizer`): Holds a `ReadOnlySpan<char>` of the source SQL. Produces tokens on demand via `NextToken()` — no intermediate token list.
- **Parser** (`SharQlParser`): Consumes tokens one at a time via the tokenizer. Builds AST nodes as it parses.
- **Tokens** (`SharQlToken`): `readonly struct` storing `(Start, Length)` offsets into the source span. Numeric literals are parsed inline (stored as `long`/`double` in the token struct).

Heap allocations occur **only** at the AST boundary — when identifier strings and AST node objects are created. The tokenization and parsing loop itself is allocation-free.

### Keyword Recognition

Keywords are matched via `ReadOnlySpan<char>.Equals(keyword, OrdinalIgnoreCase)` — no `ToUpper()`, no string interning. The classifier checks common keywords first (SELECT, FROM, WHERE) for early exit.

## File Structure

```
src/Sharc.Core/Query/SharQL/
    SharQlTokenKind.cs       Token type enum
    SharQlToken.cs           readonly struct (offsets + inline values)
    SharQlTokenizer.cs       ref struct tokenizer
    SharQlParser.cs          ref struct recursive descent parser
    SharQlParseException.cs  Parse error with position
    Ast/
        SharQlExpression.cs  All expression node types
        SelectStatement.cs   Top-level SELECT
        SelectItem.cs        Expression + alias
        TableRef.cs          Table + alias + record ID
        OrderByItem.cs       Expression + sort direction
```

## Expression Precedence

From lowest to highest:

1. `OR`
2. `AND`
3. `NOT`
4. Comparison (`=`, `!=`, `<`, `>`, `<=`, `>=`, `@@`, `IS NULL`, `BETWEEN`, `IN`, `LIKE`)
5. Addition (`+`, `-`)
6. Multiplication (`*`, `/`, `%`)
7. Unary (`-`)
8. Primary (literals, identifiers, function calls, parenthesized expressions, edge expressions, CASE, CAST, $parameters)

## Edge Operators

SharQL uses "shark tooth" operators for graph traversal instead of traditional SQL JOINs:

- **`|>`** — Forward edge. Maps to `EdgeCursor` forward traversal.
- **`<|`** — Back edge. Maps to `EdgeCursor` backward traversal.
- **`<|>`** — Bidi edge. Traverses both directions.

These compose naturally: `person:alice|>knows|>person.*` traverses the `knows` edge from `person:alice` and returns all fields of the connected `person` nodes.

## Integration Points

The parser produces AST nodes that future execution layers will consume:

- **FilterStar bridge**: `BinaryExpr` with comparison ops maps to `FilterOp` + `TypedFilterValue`
- **Graph execution**: `ArrowExpr` steps map to `EdgeCursor` + `ConceptStore` operations
- **Aggregation**: `FunctionCallExpr` (count, sum, avg, min, max) will drive a GROUP BY engine
- **Column projection**: `ColumnRefExpr` maps to `RecordDecoder` column indices via `TableInfo.GetColumnOrdinal()`

## Testing

200 tests across 5 test files:

- `SharQlTokenizerTests.cs` — 78 tests (tokenization of all token types incl. `$param`, CASE/WHEN/THEN/ELSE/END/HAVING/CAST keywords)
- `SharQlExpressionParserTests.cs` — 52 tests (expression parsing, precedence, CASE, CAST, `$param`)
- `SharQlSelectParserTests.cs` — 35 tests (full SELECT: DISTINCT, HAVING, CASE/CAST/param in queries)
- `SharQlArrowParserTests.cs` — 14 tests (edge/graph traversal expressions)
- `SharQlEdgeCaseTests.cs` — 21 tests (error handling, unicode, long queries, CASE/CAST error paths)
