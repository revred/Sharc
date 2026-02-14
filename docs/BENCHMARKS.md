# Sharc Benchmarks

Detailed performance analysis for Sharc compared to `Microsoft.Data.Sqlite` and IndexedDB.

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H (8C/16T)
> Last run: February 13, 2026

## Core Operations (5K rows)

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|---:|
| B-tree Seek | **392 ns** | 24,011 ns | **61.2x** |
| Batch 6 Seeks | **1,940 ns** | 127,526 ns | **65.7x** |
| Schema Read | **3.80 us** | 26.41 us | **6.9x** |
| Engine Init | **981 ns** | 38.68 us | **39x** |
| Sequential Scan (9 cols) | **1.54 ms** | 6.22 ms | **4.0x** |
| Integer Decode | **185 us** | 854 us | **4.6x** |
| NULL Detection | **394 us** | 1.24 ms | **3.1x** |
| WHERE Filter | **315 us** | 587 us | **1.8x** |

## Memory Allocations

| Operation | Sharc | SQLite | Winner |
|:---|---:|---:|:---:|
| Primitives | **0 B** | N/A | Sharc |
| Point Lookup (Seek) | **688 B** | 728 B | Sharc |
| Batch 6 Lookups | **1.8 KB** | 3.7 KB | Sharc |
| Sequential Scan (5K rows) | **1.35 MB** | 1.35 MB | Parity |
| Type Decode (5K ints) | **784 B** | 688 B | Parity |

## Graph Operations (5K nodes, 15K edges)

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|:---:|
| Single Node Seek | **1,475 ns** | 21,349 ns | **14.5x** |
| Batch 6 Node Seeks | **3,858 ns** | 159,740 ns | **41.4x** |
| **2-Hop BFS Traversal** | **6.27 us** | 78.49 us | **12.5x** |

## Why Sharc is Faster

| Layer Eliminated | Impact |
|:---|:---|
| P/Invoke boundary | ~200 ns per call; eliminated entirely |
| SQL parser | Zero overhead — no text to parse |
| VDBE interpreter | Direct B-tree descent replaces bytecode |

[Run the Live Browser Arena](https://revred.github.io/Sharc/)
