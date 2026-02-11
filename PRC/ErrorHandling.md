# Error Handling — Sharc

## 1. Exception Hierarchy

```
System.Exception
  └── SharcException (base for all Sharc errors)
        ├── InvalidDatabaseException
        │     File is not a valid SQLite database or header is malformed.
        │     Thrown during: Open(), OpenMemory(), header parsing
        │
        ├── CorruptPageException
        │     A specific page fails integrity checks.
        │     Properties: uint PageNumber
        │     Thrown during: page read, b-tree traversal, cell parsing
        │
        ├── SharcCryptoException
        │     Encryption/decryption failure.
        │     Thrown during: key derivation, page decryption, key verification
        │
        └── UnsupportedFeatureException
              Valid SQLite construct that Sharc doesn't handle yet.
              Thrown during: schema parsing, format version checks
```

## 2. When to Throw What

### InvalidDatabaseException

| Condition | Message |
|-----------|---------|
| File shorter than 100 bytes | "Header requires at least 100 bytes" |
| Magic string mismatch | "Invalid SQLite magic string" |
| Page size not a power of 2 | "Invalid page size: {value}" |
| Page size < 512 or > 65536 | "Page size out of range: {value}" |
| Schema format > 4 | "Unsupported schema format: {value}" |
| Text encoding not 1, 2, or 3 | "Invalid text encoding: {value}" |
| Page count is 0 or negative | "Invalid page count: {value}" |

### CorruptPageException

| Condition | Message |
|-----------|---------|
| Invalid page type flag | "Invalid b-tree page type: 0x{value:X2}" |
| Cell pointer outside page bounds | "Cell pointer {offset} exceeds page size {pageSize}" |
| Cell count exceeds page capacity | "Cell count {count} exceeds maximum for page size" |
| Overflow page number is 0 when more data expected | "Unexpected end of overflow chain" |
| Overflow page number exceeds page count | "Overflow page {number} exceeds database size" |
| Varint exceeds remaining span | "Truncated varint at offset {offset}" |

### SharcCryptoException

| Condition | Message |
|-----------|---------|
| Key verification hash mismatch | "Incorrect password or corrupted key data" |
| AEAD authentication failure | "Page decryption failed: authentication tag mismatch" |
| Unsupported KDF algorithm ID | "Unknown KDF algorithm: {id}" |
| Unsupported cipher algorithm ID | "Unknown cipher algorithm: {id}" |
| Sharc encryption header malformed | "Invalid Sharc encryption header" |

### UnsupportedFeatureException

| Condition | Feature String |
|-----------|---------------|
| WAL mode database (before Milestone 8) | "WAL journal mode" |
| UTF-16 encoding (before support added) | "UTF-16 text encoding" |
| WITHOUT ROWID tables (before support added) | "WITHOUT ROWID tables" |
| Virtual tables (FTS, R-Tree) | "Virtual table: {moduleName}" |
| Schema format 1 or 2 | "Legacy schema format {number}" |

### ArgumentException / ArgumentOutOfRangeException

| Condition | Parameter |
|-----------|-----------|
| `Open(null)` | `path` |
| `Open("")` | `path` |
| `CreateReader(null)` | `tableName` |
| `GetInt64(-1)` | `ordinal` |
| `GetInt64(ordinal >= FieldCount)` | `ordinal` |
| `ReadPage(0)` (pages are 1-based) | `pageNumber` |
| `ReadPage(n > PageCount)` | `pageNumber` |

### InvalidOperationException

| Condition | Message |
|-----------|---------|
| `Read()` called after `Dispose()` | "Reader has been disposed" |
| `GetInt64()` before first `Read()` | "No current row. Call Read() first." |
| `GetInt64()` after `Read()` returns false | "No current row. Read() returned false." |
| `GetString()` on NULL column | "Column {ordinal} is NULL. Check IsNull() first." |
| `CreateReader()` on disposed database | "Database has been disposed" |

## 3. Error Handling Patterns

### 3.1 ThrowHelper (Hot Path)

Exception construction in hot-path methods inhibits JIT inlining. Use ThrowHelper:

```csharp
// In Sharc.Core/ThrowHelper.cs
internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowInvalidDatabase(string message)
        => throw new InvalidDatabaseException(message);

    [DoesNotReturn]
    public static void ThrowCorruptPage(uint pageNumber, string message)
        => throw new CorruptPageException(pageNumber, message);

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRange(string paramName, string message)
        => throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    public static void ThrowInvalidOperation(string message)
        => throw new InvalidOperationException(message);

    [DoesNotReturn]
    public static void ThrowCryptoError(string message)
        => throw new SharcCryptoException(message);
}
```

Usage:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int Read(ReadOnlySpan<byte> data, out long value)
{
    if (data.IsEmpty)
        ThrowHelper.ThrowArgumentOutOfRange("data", "Span is empty");
    // ...
}
```

### 3.2 Validation at API Boundary

All public methods validate inputs. Internal methods trust their callers (validated by tests + debug assertions):

```csharp
// Public: validates
public SharcDataReader CreateReader(string tableName)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    ArgumentException.ThrowIfNullOrEmpty(tableName);
    // ...
}

// Internal: asserts
internal void ReadPage(uint pageNumber, Span<byte> dest)
{
    Debug.Assert(pageNumber > 0 && pageNumber <= _pageCount);
    // ...
}
```

### 3.3 Dispose Safety

- All `IDisposable` types track `_disposed` state
- All public methods on disposable types check `_disposed` first
- `Dispose()` is idempotent (safe to call multiple times)
- `Dispose()` never throws

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;

    _pageSource?.Dispose();
    _keyHandle?.Dispose();  // Zeroes key memory
}
```

## 4. Error Reporting in Tests

Tests verify both the exception type and the message content:

```csharp
[Fact]
public void Parse_InvalidMagic_ThrowsWithMessage()
{
    var data = new byte[100];
    var act = () => DatabaseHeader.Parse(data);

    act.Should().Throw<InvalidDatabaseException>()
       .WithMessage("*magic*");
}

[Fact]
public void Parse_InvalidPageType_IncludesPageNumber()
{
    var act = () => BTreePageHeader.Parse(corruptData);

    act.Should().Throw<CorruptPageException>()
       .Where(e => e.PageNumber == 5);
}
```

## 5. Error Recovery

Sharc does **not** attempt error recovery. A corrupted page or invalid database is a terminal condition for the affected operation. Rationale:

- Sharc is read-only — it cannot repair corruption
- Partial/guessed results are worse than clear errors
- Consumers can catch `SharcException` and fall back to `Microsoft.Data.Sqlite` or alert the user

The one exception: `SharcDataReader` can skip a corrupted row if a future `SharcOpenOptions.SkipCorruptRows` flag is added. This is not in v0.1 scope.
