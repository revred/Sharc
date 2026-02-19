# Benchmark Profiling Workflow

This document defines the default technique for running benchmarks during profiling and instrumentation sessions. The key principle: **run small chunks, present results immediately, analyze while the next chunk runs**.

## The Run-Analyze-Communicate Loop

```
┌─────────────────────────────────────────────────┐
│  1. PICK a chunk (2-6 benchmarks, ~2-4 min)     │
│  2. LAUNCH in background                        │
│  3. ANALYZE previous chunk results while waiting │
│  4. PRESENT findings to user                    │
│  5. REPEAT until all chunks complete             │
└─────────────────────────────────────────────────┘
```

Never run more than **6 benchmarks** in a single batch. This ensures:
- Results arrive every 2-4 minutes (not 15-30 min silence)
- Allocation anomalies surface early and can be investigated in parallel
- The user can redirect focus based on intermediate findings

## Chunk Definitions

### Comparisons Project (`bench/Sharc.Comparisons`)

| Chunk | Filter | ~Count | ~Time | Purpose |
|-------|--------|--------|-------|---------|
| Core Read | `'*CoreBenchmarks*SequentialScan*' '*CoreBenchmarks*PointLookup*'` | 4 | 2 min | Baseline read throughput |
| Core Filter | `'*CoreBenchmarks*FilterStar*' '*CoreBenchmarks*WhereFilter*' '*CoreBenchmarks*NullScan*'` | 4 | 2 min | Filter path allocation |
| Core GC/Type | `'*CoreBenchmarks*GcPressure*' '*CoreBenchmarks*TypeDecode*'` | 4 | 2 min | Zero-alloc verification |
| Core Meta | `'*CoreBenchmarks*EngineLoad*' '*CoreBenchmarks*SchemaRead*' '*CoreBenchmarks*BatchLookup*'` | 5 | 3 min | Startup + batch ops |
| Query Simple | `'*QueryRoundtrip*Simple*' '*QueryRoundtrip*Filtered*' '*QueryRoundtrip*Parameterized*'` | 6 | 3 min | Basic SQL overhead |
| Query Aggregate | `'*QueryRoundtrip*Aggregate*' '*QueryRoundtrip*Medium*'` | 4 | 2 min | GROUP BY + ORDER BY+LIMIT |
| Query Compound | `'*QueryRoundtrip*Union*' '*QueryRoundtrip*Intersect*' '*QueryRoundtrip*Except*'` | 6 | 3 min | Set operations |
| Query Cote | `'*QueryRoundtrip*Cote*' '*QueryRoundtrip*ThreeWay*'` | 4 | 2 min | CTE + multi-way ops |
| Join | `'*JoinEfficiency*'` | 2-4 | 3 min | Hash join allocation |
| View Scan | `'*ViewBenchmarks*DirectTable*' '*ViewBenchmarks*RegisteredView*' '*ViewBenchmarks*Subview*'` | 5 | 2 min | View cursor overhead |
| View Filter | `'*ViewBenchmarks*CrossType*' '*ViewBenchmarks*SameType*' '*ViewBenchmarks*Filtered*'` | 4 | 2 min | Filter + materialization |
| View SQL | `'*ViewBenchmarks*SqlQuery*'` | 2 | 1 min | SQL-on-view overhead |
| Write Small | `'*WriteBenchmarks*_1Row*' '*WriteBenchmarks*_100Rows*'` | 6 | 3 min | Small write paths |
| Write Large | `'*WriteBenchmarks*_1000Rows*' '*WriteBenchmarks*_10000Rows*'` | 4 | 4 min | Bulk write scaling |
| Write Tx | `'*WriteBenchmarks*Transaction*' '*WriteBenchmarks*InsertAndRead*'` | 4 | 3 min | Transaction overhead |
| Graph Scan | `'*GraphScanBenchmarks*'` | 8 | 3 min | Node/edge scan |
| Graph Seek | `'*GraphSeekBenchmarks*'` | 6 | 3 min | B-tree seek ops |
| Graph Traverse | `'*GraphTraversal*' '*GraphDirection*'` | 6 | 3 min | Multi-hop traversal |
| Parser Basic | `'*SharqParser*Parse_Simple*' '*SharqParser*Parse_WithWhere*' '*SharqParser*Parse_Medium*'` | 3 | 2 min | Parser baseline |
| Parser Complex | `'*SharqParser*Parse_FullPipeline*' '*SharqParser*Parse_Monster*' '*SharqParser*Parse_Complex*'` | 3 | 2 min | Heavy parse paths |
| Parser Edge | `'*SharqParser*Edge*'` | 4 | 2 min | Graph syntax parsing |
| Parser Expr | `'*SharqParser*Expr*' '*SharqParser*Throughput*'` | 4 | 2 min | Expression + throughput |

### Core Project (`bench/Sharc.Benchmarks`)

| Chunk | Filter | ~Count | ~Time | Purpose |
|-------|--------|--------|-------|---------|
| Varint | `'*VarintBenchmarks*'` | 12 | 2 min | Primitive decode/encode |
| SerialType | `'*SerialTypeCodecBenchmarks*'` | ~20 | 3 min | Type dispatch perf |
| BTreeHeader | `'*BTreePageHeaderBenchmarks*'` | 13 | 2 min | Page parsing |
| DbHeader | `'*DatabaseHeaderBenchmarks*'` | 11 | 2 min | File header parsing |
| ColumnValue | `'*ColumnValueBenchmarks*'` | 17 | 3 min | Value access + decode |
| PageTransform | `'*PageTransformBenchmarks*'` | 6 | 2 min | IO transform layer |
| GuidCodec | `'*GuidCodecBenchmarks*'` | 8 | 2 min | GUID encoding |
| Ledger | `'*LedgerBenchmark*'` | 2 | 1 min | Trust layer |
| DbOpen | `'*DatabaseOpenBenchmarks*'` | ~20 | 5 min | Startup paths |
| TableScan | `'*TableScanBenchmarks*'` | 14 | 4 min | Comparative scans |
| TypeDecode | `'*TypeDecodeBenchmarks*'` | 12 | 3 min | Type decode comparative |
| GcPressure | `'*GcPressureBenchmarks*'` | 8 | 3 min | Sustained GC impact |
| MemAlloc | `'*MemoryAllocationBenchmarks*'` | 14 | 4 min | Allocation tracking |
| Schema | `'*SchemaMetadataBenchmarks*'` | 8 | 2 min | Schema read paths |
| Header | `'*HeaderRetrievalBenchmarks*'` | 9 | 2 min | Header access |
| PageRead | `'*PageReadBenchmarks*'` | 12 | 3 min | Page IO comparative |
| Realistic | `'*RealisticWorkloadBenchmarks*'` | 12 | 4 min | End-to-end scenarios |
| WriteOps | `'*WriteOperationBenchmarks*'` | 8 | 3 min | Update/delete comparative |

## Running a Chunk

```bash
# Single filter (most common)
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter '*CoreBenchmarks*SequentialScan*'

# Multiple filters (for mixed chunks)
dotnet run -c Release --project bench/Sharc.Comparisons -- \
  --filter '*CoreBenchmarks*FilterStar*' '*CoreBenchmarks*WhereFilter*'

# List what a filter matches before running
dotnet run -c Release --project bench/Sharc.Comparisons -- --list flat --filter '*ViewBenchmarks*'
```

## Profiling Protocol

### Phase 1: Allocation Survey (pick 4-6 chunks covering the target area)

1. Start with the **cheapest chunk** in the area (e.g., Core Read for read-path profiling)
2. Launch in background, present the filter and expected count to user
3. While waiting: read source code of the hot path being benchmarked
4. When results arrive: build an **allocation table** (Benchmark | Mean | Allocated | GC)
5. Identify any result that allocates more than expected → flag for investigation
6. Launch next chunk, investigate flagged results in parallel

### Phase 2: Deep Dive (targeted investigation of anomalies)

1. For each flagged benchmark, trace the allocation path through source code
2. Build a **component-level breakdown** (which constructor, which array, which dictionary)
3. Compare to a "budget" — what's the minimum allocation for this operation?
4. Identify the **dominant allocator** (single largest contributor, usually >30% of total)
5. Propose concrete optimization (inline fields, pooling, capacity hints, skip alloc)

### Phase 3: Baseline Document (update PRC/PerformanceBaseline.md)

1. Organize results into **allocation tiers** (see PerformanceBaseline.md for the tier system)
2. Add Sharc vs SQLite comparison for paired benchmarks
3. Document optimization opportunities with expected savings
4. Record as the baseline for future regression detection

## Key Conventions

- **Never run more than 6 benchmarks per batch** — prefer 2-4 for rapid feedback
- **Never run the full suite** — always use `--filter` or `--tier micro/mini`
- **Never run two BenchmarkDotNet processes in parallel** — they conflict
- **Always present results as they arrive** — don't wait for all chunks to complete
- **Always track GC generations** — Gen2 collections indicate LOH pressure
- **Use `--list flat` to verify filters** before committing to a long run
- **Read the source code of what you're benchmarking** — helps interpret allocation numbers
- **Compare to SQLite numbers carefully** — SQLite's managed allocation only measures the .NET wrapper, not native memory

## MCP Tool Integration

The `tools/Sharc.Context` MCP server exposes benchmark capabilities that can be used for automated profiling:

```
BenchmarkTool.RunBenchmarks(filter, job)       — Run with streaming output
BenchmarkTool.RunGraphBenchmarks(filter, job)   — Graph-specific benchmarks
BenchmarkTool.ReadBenchmarkResults(className)   — Read markdown reports
BenchmarkTool.ListBenchmarkResults()            — List available results
```

Job types: `short` (3 iterations, fast feedback), `medium` (default BDN), `dry` (validate setup only).

Use `short` job for allocation profiling — iteration count doesn't affect allocation measurements, and you get results 3x faster.
