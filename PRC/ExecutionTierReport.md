# Execution Tier Benchmark Report - DIRECT vs CACHED vs JIT

**Date:** 2026-02-25 (latest full execution-tier run) + 2026-02-25 (focused micro addendum)
**Hardware:** 11th Gen Intel Core i7-11800H, 8C/16T
**Runtime:** .NET 10.0.2
**Dataset:** 2,500 rows x 8 columns (`users_a`)

Sources:
- `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.ExecutionTierBenchmarks-report.csv`
- `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.FocusedPerfBenchmarks-report.csv`

---

## Executive Summary

- Filtered queries: `CACHED`, `JIT`, and manual prepared/JIT paths all beat `DIRECT`, with `CACHED`/`JIT` at 0 B allocation.
- Parameterized queries: `CACHED` is now the fastest measured mode in this run (0.85x vs `DIRECT`) with 56 B allocation.
- Full-scan queries: `JIT` is effectively tied with `DIRECT`; `CACHED` remains slower on no-filter scans.
- String-heavy narrow projections: all tiers are near parity; string materialization dominates (~97 KB).

---

## Benchmark Results

### A. Filtered scan - `SELECT id, name, age FROM users_a WHERE age > 30`

| Method | Mean | Ratio | Allocated |
| :--- | ---: | ---: | ---: |
| `DIRECT: Query(sql)` | 117.48 us | 1.00 | 472 B |
| `CACHED: Query(hint sql)` | **90.62 us** | **0.77** | **0 B** |
| `JIT: Query(hint sql)` | **85.66 us** | **0.73** | **0 B** |
| `Manual Prepare.Execute()` | **87.53 us** | **0.75** | **0 B** |
| `Manual Jit.Query()` | **85.39 us** | **0.73** | 48 B |

### B. Full scan - `SELECT * FROM users_a`

| Method | Mean | Ratio | Allocated |
| :--- | ---: | ---: | ---: |
| `DIRECT: SELECT *` | 64.20 us | 1.00 | 416 B |
| `CACHED: SELECT *` | 77.73 us | 1.21 | **0 B** |
| `JIT: SELECT *` | **64.07 us** | **1.00** | **0 B** |

### C. Narrow projection - `SELECT name FROM users_a WHERE age > 30`

| Method | Mean | Ratio | Allocated |
| :--- | ---: | ---: | ---: |
| `DIRECT: narrow` | **135.33 us** | 1.00 | 97,720 B |
| `CACHED: narrow` | 138.35 us | 1.02 | 97,248 B |
| `JIT: narrow` | 135.83 us | 1.00 | 97,248 B |

### D. Parameterized filter - `SELECT ... WHERE age > $minAge`

| Method | Mean | Ratio | Allocated |
| :--- | ---: | ---: | ---: |
| `DIRECT: parameterized` | 97.27 us | 1.00 | 528 B |
| `CACHED: parameterized` | **82.59 us** | **0.85** | **56 B** |
| `Manual Prepare: parameterized` | **95.83 us** | **0.99** | **56 B** |

---

## Addendum: Focused Prepared/Closure Micro-Optimizations

Command:

```bash
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *FocusedPerfBenchmarks*
```

### Prepared parameter cache key path

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :---: | ---: | ---: | ---: | ---: |
| 1 | 22.6216 / 64 B | 11.9880 / 0 B | **-47.01%** | **-100%** |
| 8 | 171.6415 / 184 B | 164.4888 / 64 B | **-4.17%** | **-65.22%** |
| 32 | 727.5258 / 376 B | 711.7530 / 64 B | **-2.17%** | **-82.98%** |
| 128 | 3,391.4931 / 1,144 B | 3,305.2322 / 64 B | **-2.54%** | **-94.41%** |

### Candidate span path

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :---: | ---: | ---: | ---: | ---: |
| 1 | 5.1912 / 32 B | 0.5796 / 0 B | **-88.83%** | **-100%** |
| 8 | 7.2545 / 88 B | 0.9640 / 0 B | **-86.71%** | **-100%** |
| 32 | 14.3232 / 280 B | 0.5796 / 0 B | **-95.95%** | **-100%** |
| 128 | 40.3487 / 1,048 B | 0.5719 / 0 B | **-98.58%** | **-100%** |

---

## Recommendations

1. Default to `CACHED` for parameterized query paths; it is faster than `DIRECT` in this run and materially lower allocation.
2. Use `JIT` or manual prepared/JIT APIs for hot filtered loops where repeated query shape is stable.
3. Keep `DIRECT` as baseline for simple no-filter scans when convenience of hints is not needed.
4. For narrow/string-heavy projections, prioritize data-access pattern changes before execution-tier tuning.
