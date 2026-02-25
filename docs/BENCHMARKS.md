# Sharc Benchmarks

Detailed performance comparison: Sharc vs Microsoft.Data.Sqlite.

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H (8C/16T)
> Latest full comparative run: February 25, 2026.
> Latest focused micro-bench run: February 25, 2026.

---

## Core Operations (5K rows, 9-column `users` table)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Point lookup | **38.14 ns** | 23,226.52 ns | **609x** | **0 B** | 728 B |
| Batch 6 lookups | **626.14 ns** | 122,401.32 ns | **195x** | **0 B** | 3,712 B |
| Random lookup | **217.71 ns** | 23,415.67 ns | **108x** | **0 B** | 832 B |
| Engine load | **192.76 ns** | 22,663.42 ns | **118x** | 1,592 B | 1,160 B |
| Schema read | **2,199.88 ns** | 25,058.57 ns | **11.4x** | 5,032 B | 2,536 B |
| Sequential scan | **875.95 us** | 5,630.27 us | **6.4x** | 1,411,576 B | 1,412,320 B |
| Type decode | **146.42 us** | 774.46 us | **5.3x** | **0 B** | 688 B |
| NULL scan | **148.75 us** | 727.66 us | **4.9x** | **0 B** | 688 B |
| WHERE filter | **261.73 us** | 541.54 us | **2.1x** | **0 B** | 720 B |
| GC pressure | **156.31 us** | 766.46 us | **4.9x** | **0 B** | 688 B |

> Summary: core read paths remain the strongest area, with hot-path lookup/filter/decode benchmarks at 0 B managed allocation for Sharc.

---

## Index-Accelerated WHERE (5K users)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| WHERE int = N (indexed) | **1.036 us** | 31.589 us | **30.5x** | 1,272 B | 872 B |
| WHERE int = N (Sharc full scan baseline) | **401.126 us** | -- | **387x vs indexed** | 616 B | -- |
| WHERE text = T (indexed) | **105.833 us** | 248.550 us | **2.35x** | 1,168 B | 728 B |
| WHERE text = T (Sharc full scan baseline) | **148.561 us** | -- | **1.40x vs indexed** | 472 B | -- |
| WHERE unindexed column | **524.860 us** | 868.330 us | **1.65x** | 728 B | 720 B |

---

## Graph Operations (5K nodes, 15K edges)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Seek single node | **7.071 us** | 70.553 us | **10.0x** | 888 B | 648 B |
| Seek batch 6 nodes | **14.767 us** | 203.713 us | **13.8x** | 3,224 B | 3,312 B |
| Open -> seek -> close | **72.477 us** | 449.353 us | **6.2x** | 10,424 B | 2,112 B |
| Scan all nodes | **2.285 ms** | 3.421 ms | **1.5x** | 958,520 B | 959,280 B |
| Scan node projection | **1.946 ms** | 2.469 ms | **1.27x** | 480,488 B | 480,752 B |
| Scan all edges | **4.854 ms** | 7.941 ms | **1.64x** | 1,440,000 B | 1,440,768 B |
| Edge count by kind | **1.721 ms** | 2.849 ms | **1.66x** | **0 B** | 744 B |
| BFS 2-hop traversal | **45.59 us** | 205.67 us | **4.5x** | 800 B | 2,952 B |

---

## Query Pipeline (Query API full SQL roundtrip)

> 2,500 rows/table. Compound queries use two tables with 500 overlapping rows.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| `SELECT * FROM users_a` | **595.3 us** | 730.2 us | **1.23x** | **416 B** | 736 B |
| `SELECT WHERE age > 30` | **900.4 us** | 1,085.8 us | **1.21x** | **97,720 B** | 98,016 B |
| `UNION ALL (2x2500 rows)` | **2,431.0 us** | 2,873.7 us | **1.18x** | 414,432 B | 414,032 B |
| `UNION (dedup, 500 overlap)` | 1,942.2 us | **1,940.6 us** | ~1.00x | 1,504 B | 792 B |
| `GROUP BY + COUNT + AVG` | 1,706.0 us | **553.1 us** | 0.32x | 4,576 B | 968 B |
| `WHERE + ORDER BY + LIMIT 100` | 1,012.8 us | **343.2 us** | 0.34x | 42,240 B | 5,656 B |
| `UNION ALL + ORDER BY + LIMIT 50` | 1,899.7 us | **460.0 us** | 0.24x | 32,224 B | 3,168 B |
| `INTERSECT` | 3,316.1 us | **1,317.7 us** | 0.40x | 1,192 B | 792 B |
| `EXCEPT` | 3,413.6 us | **1,193.0 us** | 0.35x | 1,192 B | 792 B |
| `3-way UNION ALL` | 1,930.6 us | **1,494.0 us** | 0.77x | 2,000 B | 824 B |
| `CTE -> SELECT WHERE` | 726.7 us | **502.8 us** | 0.69x | **31,400 B** | 31,768 B |
| `CTE + UNION ALL` | 1,210.5 us | **809.1 us** | 0.67x | 1,248 B | 864 B |
| `parameterized WHERE` | 915.3 us | **830.8 us** | 0.91x | **81,512 B** | 81,944 B |

> Summary: in this run, Sharc wins 3 of 13 query roundtrip benchmarks, is near tie on `UNION`, and loses primarily on sort-heavy and set-heavy query shapes.

---

## Execution Tier Benchmarks (DIRECT vs CACHED vs JIT)

| Scenario | Method | Mean | Ratio vs DIRECT | Allocated |
| :--- | :--- | ---: | ---: | ---: |
| Filtered (`WHERE age > 30`) | DIRECT | 117.48 us | 1.00 | 472 B |
| Filtered (`WHERE age > 30`) | CACHED | **90.62 us** | **0.77** | **0 B** |
| Filtered (`WHERE age > 30`) | JIT | **85.66 us** | **0.73** | **0 B** |
| Filtered (`WHERE age > 30`) | Manual Prepare | **87.53 us** | **0.75** | **0 B** |
| Filtered (`WHERE age > 30`) | Manual Jit | **85.39 us** | **0.73** | 48 B |
| Full scan (`SELECT *`) | DIRECT | 64.20 us | 1.00 | 416 B |
| Full scan (`SELECT *`) | CACHED | 77.73 us | 1.21 | **0 B** |
| Full scan (`SELECT *`) | JIT | **64.07 us** | **1.00** | **0 B** |
| Narrow string projection | DIRECT | **135.33 us** | 1.00 | 97,720 B |
| Narrow string projection | CACHED | 138.35 us | 1.02 | 97,248 B |
| Narrow string projection | JIT | 135.83 us | 1.00 | 97,248 B |
| Parameterized (`WHERE age > $minAge`) | DIRECT | 97.27 us | 1.00 | 528 B |
| Parameterized (`WHERE age > $minAge`) | CACHED | **82.59 us** | **0.85** | **56 B** |
| Parameterized (`WHERE age > $minAge`) | Manual Prepare | **95.83 us** | **0.99** | **56 B** |

---

## Write Benchmarks

### INSERT path (`Sharc.Comparisons.WriteBenchmarks`)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Insert 1 row | **4.084 ms** | 4.806 ms | **1.18x** | 16.28 KB | 8.03 KB |
| Insert 100 rows | **4.360 ms** | 5.169 ms | **1.19x** | 28.17 KB | 71.2 KB |
| Transaction 100 rows | **4.290 ms** | 5.314 ms | **1.24x** | 26.63 KB | 71.23 KB |
| Insert 1K rows | **5.083 ms** | 6.605 ms | **1.30x** | 124.59 KB | 605.55 KB |
| Insert 10K rows | **11.251 ms** | 16.744 ms | **1.49x** | 2,570.24 KB | 5,952.78 KB |
| Insert + read 100 rows | **4.570 ms** | 5.272 ms | **1.15x** | 47.5 KB | 71.92 KB |

### UPDATE/DELETE path (`Sharc.Benchmarks.Comparative.WriteOperationBenchmarks`)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Delete single row | **2.148 ms** | 12.245 ms | **5.7x** | 10.95 KB | 1.66 KB |
| Delete 100 rows | **2.966 ms** | 26.464 ms | **8.9x** | 13.26 KB | 32.62 KB |
| Update single row | **2.207 ms** | 12.788 ms | **5.8x** | 28.08 KB | 1.72 KB |
| Update 100 rows | **4.130 ms** | 27.418 ms | **6.6x** | 33.8 KB | 32.68 KB |

> Write-path allocation note: SQLite numbers include only managed wrapper allocations. Native engine allocations are outside `MemoryDiagnoser` visibility.

---

## Focused Micro-Optimizations (2026-02-25)

Command:

```bash
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *FocusedPerfBenchmarks*
```

### Candidate span path

Legacy: `ToArray().AsSpan()`
Optimized: `CollectionsMarshal.AsSpan(candidates)`

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :---: | ---: | ---: | ---: | ---: |
| 1 | 5.1912 / 32 B | 0.5796 / 0 B | **-88.83%** | **-100%** |
| 8 | 7.2545 / 88 B | 0.9640 / 0 B | **-86.71%** | **-100%** |
| 32 | 14.3232 / 280 B | 0.5796 / 0 B | **-95.95%** | **-100%** |
| 128 | 40.3487 / 1,048 B | 0.5719 / 0 B | **-98.58%** | **-100%** |

### Prepared-parameter cache key path

Legacy: `List+Sort+Indexer`
Optimized: `ArrayPool` pair-sort

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :---: | ---: | ---: | ---: | ---: |
| 1 | 22.6216 / 64 B | 11.9880 / 0 B | **-47.01%** | **-100%** |
| 8 | 171.6415 / 184 B | 164.4888 / 64 B | **-4.17%** | **-65.22%** |
| 32 | 727.5258 / 376 B | 711.7530 / 64 B | **-2.17%** | **-82.98%** |
| 128 | 3,391.4931 / 1,144 B | 3,305.2322 / 64 B | **-2.54%** | **-94.41%** |

---

## Reproduce the Benchmarks

```bash
# Core engine comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *CoreBenchmarks*

# Query roundtrip comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *QueryRoundtripBenchmarks*

# Graph comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *GraphSeekBenchmarks* *GraphScanBenchmarks* *GraphTraversalBenchmarks*

# Execution tier comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *ExecutionTierBenchmarks*

# Index acceleration comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *IndexAcceleratedBenchmarks*

# Write comparisons
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *WriteBenchmarks*

# Focused micro-bench suite
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *FocusedPerfBenchmarks*
```

Results used in this document:
- `artifacts/benchmarks/comparisons/results/*.csv`
- `artifacts/benchmarks/core/results/Sharc.Benchmarks.Comparative.WriteOperationBenchmarks-report.csv`

---

## Fairness Notes

- All values above are measured, not estimated.
- `MemoryDiagnoser` reports managed allocations only; SQLite native allocations are not included.
- BenchmarkDotNet warmup and iteration controls are used for all comparative suites.
