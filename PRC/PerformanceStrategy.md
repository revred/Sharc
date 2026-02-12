# Performance Strategy — Sharc

## 1. Performance Goals

| Operation | Target | Measurement |
|-----------|--------|-------------|
| Varint decode | >200M ops/sec | BenchmarkDotNet, single-core |
| Page read (cached) | <50 ns | BenchmarkDotNet |
| Page read (file, warm OS cache) | <2 µs | BenchmarkDotNet |
| Row decode (5-column record) | <200 ns | BenchmarkDotNet |
| Full table scan (100K rows, 5 cols) | Within 3× of Microsoft.Data.Sqlite | Comparative benchmark |
| Memory allocation per row | 0 bytes (primitive cols) | MemoryDiagnoser |
| Memory allocation per scan | O(1) (excluding string/blob materialization) | MemoryDiagnoser |

## 2. Zero-Allocation Design

### 2.1 Span-Based Data Flow

The entire read path from page bytes to column values operates on `ReadOnlySpan<byte>`:

```
Page buffer (byte[])
    │
    ▼ ReadOnlySpan<byte>
BTree cell extraction
    │
    ▼ ReadOnlySpan<byte> (payload slice)
Record header parsing
    │
    ▼ ReadOnlySpan<byte> (column value slice)
Typed accessor (GetInt64 → reads from span, no allocation)
```

No intermediate `byte[]` copies are created for inline payloads.

### 2.2 Value Types for Hot-Path Structures

All frequently-created parse results are `readonly struct`:
- `DatabaseHeader` — parsed once per open
- `BTreePageHeader` — parsed once per page visit
- `ColumnValue` — created per column per row (inline storage, no heap)

### 2.3 ArrayPool for Overflow Assembly

When a record spans overflow pages, the full payload must be assembled:

```csharp
// Rent from pool
byte[] buffer = ArrayPool<byte>.Shared.Rent(totalPayloadSize);
try
{
    // Assemble payload into buffer
    AssembleOverflowPayload(buffer, inlinePayload, overflowPages);
    // Decode record from assembled buffer
    var columns = decoder.DecodeRecord(buffer.AsSpan(0, totalPayloadSize));
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
}
```

### 2.4 Avoiding Common Allocation Traps

| Trap | Mitigation |
|------|-----------|
| `string.Format` / interpolation in exceptions | Pre-allocate message strings; use `ThrowHelper` pattern |
| LINQ in hot paths | Manual loops with span indexing |
| Boxing `int`/`long` in generic code | `ColumnValue` struct stores primitives inline |
| `IEnumerable<T>` yields | Return arrays or use cursor pattern |
| Closure captures in lambdas | Avoid lambdas in hot paths entirely |
| `params` array allocation | Use overloads or `ReadOnlySpan<T>` params |

## 3. ThrowHelper Pattern

To keep hot-path methods small and JIT-inlineable, exception throws are moved to separate `[DoesNotReturn]` methods:

```csharp
public static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowCorruptPage(uint pageNumber, string message)
        => throw new CorruptPageException(pageNumber, message);

    [DoesNotReturn]
    public static void ThrowInvalidDatabase(string message)
        => throw new InvalidDatabaseException(message);
}
```

This prevents the JIT from marking the calling method as "may throw" which inhibits inlining.

## 4. Page Cache Strategy

### 4.1 LRU Cache Design

```
CachedPageSource
  ├── Dictionary<uint, CacheEntry> _lookup    — O(1) page lookup
  ├── LinkedList<CacheEntry> _lru             — O(1) eviction
  └── int _capacity                           — max cached pages
```

**Cache entry**: Holds a `byte[]` rented from `ArrayPool<byte>.Shared`. On eviction, the buffer is returned to the pool.

### 4.2 Cache Sizing

Default: 2000 pages × 4 KiB = ~8 MiB. Configurable via `SharcOpenOptions.PageCacheSize`.

| Workload | Recommended Cache |
|----------|------------------|
| Small DB (<10 MiB) | 0 (use MemoryPageSource) |
| Medium DB scan | 200–500 pages |
| Large DB, single table | 2000 pages (default) |
| Multiple concurrent readers | 5000+ pages |

### 4.3 Memory-Backed Databases

When opened via `OpenMemory()`, the `MemoryPageSource` directly slices the user-provided buffer. No cache is needed (set `PageCacheSize = 0` automatically). Page reads are a pointer offset + length calculation — essentially free.

## 5. BinaryPrimitives for Big-Endian Reads

SQLite uses big-endian encoding for all multi-byte integers. .NET's `BinaryPrimitives` provides branchless, hardware-optimized big-endian reads:

```csharp
// Instead of: (data[0] << 8) | data[1]
ushort value = BinaryPrimitives.ReadUInt16BigEndian(data);

// Instead of: (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]
uint value = BinaryPrimitives.ReadUInt32BigEndian(data);
```

These compile to single `bswap` instructions on x86 and are free on big-endian hardware.

## 6. Column Projection Optimization

When a reader is created with specific columns, the record decoder can skip decoding unused columns:

```csharp
// User requests only columns 0 and 3 out of 10
using var reader = db.CreateReader("events", "id", "timestamp");

// RecordDecoder skips columns 1, 2, 4, 5, 6, 7, 8, 9
// Only reads header to find offsets, then jumps to columns 0 and 3
```

This is particularly effective for wide tables where most columns are unused.

Implementation:
1. Parse all serial types in the header (cheap — just varint reads)
2. Calculate byte offset of each column (cumulative serial type sizes)
3. Only decode the requested column positions

## 7. Varint Decoder Optimization

The varint decoder is the single most-called function in the system. Optimizations:

### 7.1 Fast Path for Single-Byte Varints

Most varints in real databases are 1–2 bytes (page numbers, small rowids, common serial types). The fast path checks the MSB:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int Read(ReadOnlySpan<byte> data, out long value)
{
    byte first = data[0];
    if (first < 0x80)  // Single byte — most common case
    {
        value = first;
        return 1;
    }
    return ReadMultiByte(data, out value);  // Outlined slow path
}
```

### 7.2 Branchless Two-Byte Path

```csharp
if ((data[1] & 0x80) == 0)  // Two bytes
{
    value = ((first & 0x7FL) << 7) | data[1];
    return 2;
}
```

## 8. Benchmark Suite

### 8.1 Micro-Benchmarks (Sharc.Benchmarks)

```csharp
[Benchmark] long DecodeVarint_1Byte()
[Benchmark] long DecodeVarint_2Byte()
[Benchmark] long DecodeVarint_9Byte()
[Benchmark] void ParseDatabaseHeader()
[Benchmark] void ParseBTreePageHeader()
[Benchmark] void DecodeRecord_5Columns()
[Benchmark] void ReadPage_Cached()
[Benchmark] void ReadPage_File()
[Benchmark] void ReadPage_Memory()
```

### 8.2 Macro-Benchmarks

```csharp
[Benchmark] void FullTableScan_100K_Rows_Sharc()
[Benchmark] void FullTableScan_100K_Rows_MicrosoftDataSqlite()  // Comparison
[Benchmark] void SchemaEnumeration_50Tables()
[Benchmark] void OpenClose_SmallDatabase()
[Benchmark] void OpenClose_LargeDatabase()
```

### 8.3 Allocation Profiling

Every benchmark uses `[MemoryDiagnoser]` to track:
- Total bytes allocated
- Gen0/Gen1/Gen2 collections
- Per-operation allocation (should be 0 for primitive reads)

### 8.4 Regression Detection

Benchmark results are saved as JSON baselines. CI compares against baseline and fails if:
- Any micro-benchmark regresses >10%
- Any macro-benchmark regresses >20%
- Per-row allocation increases above 0

## 9. Platform-Specific Considerations

| Platform | Notes |
|----------|-------|
| x86-64 (Windows/Linux) | AES-NI for encryption, SSSE3 for byte-swap, best performance |
| ARM64 (macOS/Linux) | NEON equivalents available, BinaryPrimitives auto-adapts |
| WASM (Blazor) | No file I/O — memory-backed only, no encryption hardware |
| x86 (32-bit) | Supported but not optimized — 64-bit long operations slower |

## 10. Profiling Workflow

1. **Identify hot path**: BenchmarkDotNet identifies slowest operations
2. **Allocation audit**: MemoryDiagnoser confirms zero-allocation paths
3. **Disassembly review**: `[DisassemblyDiagnoser]` shows JIT output for critical methods
4. **Guided optimization**: Only optimize methods that appear in profiler top-10
5. **Regression test**: Re-run full benchmark suite after optimization
6. **Document**: Record optimization in `DecisionLog.md` with before/after numbers
