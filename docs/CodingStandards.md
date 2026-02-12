# Coding Standards — Sharc

## 1. Language & Framework

- **C# 13** with .NET 10.0 SDK
- **Nullable reference types**: enabled in all projects
- **Implicit usings**: enabled
- **Unsafe code**: allowed in Sharc.Core and Sharc.Crypto only, with justification

## 2. Naming Conventions

### Types

| Kind | Convention | Example |
|------|-----------|---------|
| Public class | PascalCase, `sealed` by default | `SharcDatabase` |
| Public struct | PascalCase, `readonly` by default | `DatabaseHeader` |
| Public interface | `I` prefix + PascalCase | `IPageSource` |
| Public enum | PascalCase | `BTreePageType` |
| Enum members | PascalCase | `LeafTable` |
| Internal class | PascalCase | `CellParser` |
| Static helper class | PascalCase, `static` | `VarintDecoder` |

### Members

| Kind | Convention | Example |
|------|-----------|---------|
| Public property | PascalCase | `PageSize` |
| Public method | PascalCase | `CreateReader` |
| Private field | `_camelCase` | `_disposed` |
| Local variable | `camelCase` | `pageNumber` |
| Const | PascalCase | `MaxVarintBytes` |
| Static readonly | PascalCase | `MagicBytes` |
| Parameter | `camelCase` | `tableName` |
| Type parameter | `T` prefix | `TValue` |

### Files

- One public type per file (exceptions: closely related small types like enums)
- File name matches type name: `SharcDatabase.cs`, `BTreePageHeader.cs`
- Test files: `{TypeUnderTest}Tests.cs`

## 3. Type Design

### Classes vs Structs

Use **struct** when:
- The type is a small (<64 bytes) value representing parsed data
- Instances are short-lived (stack-allocated, not stored in collections)
- No mutable state after construction
- Examples: `DatabaseHeader`, `BTreePageHeader`, `ColumnValue`

Use **class** when:
- The type owns resources (`IDisposable`)
- The type has mutable state (cursors, caches)
- The type is large or has reference-type fields
- Examples: `SharcDatabase`, `BTreeCursor`, `CachedPageSource`

### Immutability

- Struct fields: always `readonly`
- Class fields: `readonly` where possible
- Properties: `init` for construction-time-only values
- Collections: expose as `IReadOnlyList<T>`

### Sealed by Default

All classes are `sealed` unless explicitly designed for inheritance:
```csharp
public sealed class SharcDatabase : IDisposable  // ✓ sealed
internal sealed class CellParser                  // ✓ sealed
```

## 4. Method Design

### Parameter Order

1. The primary data input (e.g., `ReadOnlySpan<byte> data`)
2. Configuration/context (e.g., `uint pageNumber`)
3. Output parameters (e.g., `out long value`)

### Span Parameters

```csharp
// ✓ Accept ReadOnlySpan<byte> for read-only input
public static int Read(ReadOnlySpan<byte> data, out long value)

// ✓ Accept Span<byte> for writable output
public static int Write(Span<byte> destination, long value)

// ✗ Do NOT accept byte[] when a span would work
public static int Read(byte[] data, out long value)  // Wrong
```

### Inlining

Apply `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to:
- Methods under ~32 bytes of IL
- Methods called millions of times in tight loops
- Trivial property getters in structs

Do NOT inline:
- Methods that throw exceptions (use ThrowHelper instead)
- Methods with complex branching
- Methods over ~64 bytes of IL

### Output Pattern

For methods that parse structured data from a span, prefer the `out` pattern:
```csharp
int bytesConsumed = VarintDecoder.Read(data, out long value);
```

Over the tuple pattern:
```csharp
(long value, int bytesConsumed) = VarintDecoder.Read(data);  // Avoid — less idiomatic for hot paths
```

## 5. Error Handling

### Exception Usage

- **Do throw** `ArgumentException` for invalid API inputs
- **Do throw** `InvalidDatabaseException` for file format violations
- **Do throw** `CorruptPageException` for page-level data integrity failures
- **Do NOT** catch exceptions in library code unless you can meaningfully handle them
- **Do NOT** use exception-driven control flow (no `try { parse } catch { return default }`)

### Validation

```csharp
// ✓ Guard at public API boundary
public static DatabaseHeader Parse(ReadOnlySpan<byte> data)
{
    if (data.Length < 100)
        ThrowHelper.ThrowInvalidDatabase("Header requires at least 100 bytes");
    if (!HasValidMagic(data))
        ThrowHelper.ThrowInvalidDatabase("Invalid SQLite magic string");
    // ...parse...
}

// ✓ Assert at internal boundary (debug only)
Debug.Assert(pageNumber > 0, "Page numbers are 1-based");
```

### ThrowHelper

All exception throws in hot-path code use the ThrowHelper pattern:
```csharp
internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowInvalidDatabase(string message)
        => throw new InvalidDatabaseException(message);

    [DoesNotReturn]
    public static void ThrowCorruptPage(uint pageNumber, string message)
        => throw new CorruptPageException(pageNumber, message);
}
```

## 6. Documentation

### XML Docs

Required on:
- All public types
- All public methods and properties
- All public constructors

Optional on:
- Internal types (but encouraged for complex ones)
- Private members

### Format

```csharp
/// <summary>
/// Decodes a SQLite varint from the given span.
/// </summary>
/// <param name="data">Input bytes. Must contain at least 1 byte.</param>
/// <param name="value">The decoded 64-bit value.</param>
/// <returns>Number of bytes consumed (1–9).</returns>
/// <exception cref="ArgumentException">Span is empty.</exception>
```

### Decision Comments

When a non-obvious choice is made, document it inline:
```csharp
// DECISION: Treat reserved serial types 10,11 as errors rather than silently
// returning NULL. Rationale: these types indicate a corrupt or future-format
// database that Sharc cannot correctly interpret. See DecisionLog.md ADR-007.
```

## 7. Test Code Standards

### Naming

```
[MethodUnderTest]_[Scenario]_[ExpectedResult]
```

### Structure (Arrange-Act-Assert)

```csharp
[Fact]
public void Read_SingleByteZero_ReturnsZeroAndConsumesOneByte()
{
    // Arrange
    ReadOnlySpan<byte> data = [0x00];

    // Act
    var consumed = VarintDecoder.Read(data, out var value);

    // Assert
    Assert.Equal(0L, value);
    Assert.Equal(1, consumed);
}
```

### Test Independence

- Each test creates its own data — no shared mutable state
- Use `[Theory]` + `[InlineData]` for parameterized cases
- Use helper methods (e.g., `CreateValidHeader()`) for test data construction
- Tests must pass in any order and in parallel

### Assertion Library

Use **plain xUnit Assert** for all assertions (ADR-012/013):
```csharp
Assert.Equal(42L, value);
Assert.Throws<InvalidDatabaseException>(() => Parse(bad));
Assert.Equal(4096, header.PageSize);
```

## 8. Project Configuration

### Directory.Build.props (if needed)

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningLevel>7</WarningLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

### Analyzer Configuration

- Enable all nullable warnings
- Enable all code style warnings
- CA1062 (validate arguments) — suppress in `internal` code, enable in `public` code

## 9. Git Conventions

### Commit Messages

```
feat(core): implement varint decoder with span-based read
fix(btree): handle zero-cell leaf pages without crash
test(records): add serial type edge cases for reserved types
perf(varint): optimize single-byte fast path with branch prediction
docs(prc): add performance strategy document
```

### Branch Names

```
feat/milestone-1-primitives
fix/overflow-page-chain
perf/varint-fast-path
docs/encryption-spec
```
