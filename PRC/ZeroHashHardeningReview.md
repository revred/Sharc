# Zero.Hash Hardening Review (2026-02-25)

## Scope
Review the current `dev` branch implementation and benchmark evidence behind the Zero.Hash paper claims in `PRC/ZeroAllocHashJoinV1.md`.

## Changes Applied in This Pass
1. Added `PooledBitArray.TrySet(int)` to detect first-time matches without extra structures.
2. Added matched-count short-circuit in tiered FULL OUTER join paths:
   - Skip residual scan when all build rows matched.
   - Stop scanning as soon as all unmatched rows are emitted.
3. Added a stable benchmark profile for join comparisons:
   - `LaunchCount=1`, `WarmupCount=8`, `IterationCount=24`.

## Commands Executed
```powershell
# targeted correctness

dotnet test tests/Sharc.Tests/Sharc.Tests.csproj -c Release --filter "FullyQualifiedName~TieredHashJoin|FullyQualifiedName~PooledBitArray"

# focused stable benchmark slice

dotnet run -c Release --project bench/Sharc.Comparisons -- --filter "*FullOuterJoin_TieredHashJoin*" "*LeftJoin_Baseline*"
```

## Current Measured Results (Stable Job)
Source: `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.JoinEfficiencyBenchmarks-report-github.md`

| Method | UserCount | Mean | StdDev | Allocated |
|---|---:|---:|---:|---:|
| FullOuterJoin_TieredHashJoin | 1000 | 1.106 ms | 0.0675 ms | 630.25 KB |
| LeftJoin_Baseline | 1000 | 1.286 ms | 0.1885 ms | 630.13 KB |
| FullOuterJoin_TieredHashJoin | 5000 | 5.673 ms | 0.2001 ms | 2402.7 KB |
| LeftJoin_Baseline | 5000 | 6.769 ms | 1.3706 ms | 3188.4 KB |

Interpretation on this machine/run:
- 1k rows: FULL OUTER is faster with lower variance; allocations are effectively tied.
- 5k rows: FULL OUTER is faster, allocates ~24.6% less, and shows much tighter spread.

## Findings (ordered by severity)

### High: publication artifacts still contain stale point estimates
- `PRC/ZeroAllocHashJoinV1.md`, `PRC/DecisionLog.md`, and `PRC/ZeroAllocDiffJoin.md` still carry historical numbers and fixed wording.
- These should be updated to clearly separate historical vs current measurements.

### High: Tier III naming/docs still say "DestructiveProbe" while implementation is read-only probe
- Runtime behavior is read-only lookup + bit-array tracking.
- Enum/test/comment nomenclature is still from earlier design language.

### Medium: benchmark data shape remains mostly all-matched
- Current generator maps all orders to existing users.
- Add unmatched and duplicate-hot-key scenarios to stress residual and duplicate behavior more directly.

### Low: correctness is currently strong
- Targeted tests passed: 57/57.

## Next Hardening Steps (recommended)

1. Rename Tier III nomenclature to read-only terminology in code/tests/docs.
2. Add benchmark scenarios:
   - unmatched-build heavy,
   - unmatched-probe heavy,
   - duplicate probe hot key,
   - null-key heavy.
3. Add kernel-only join microbenchmark (`TieredHashJoin.Execute`) alongside end-to-end SQL benchmark.
4. Add benchmark provenance block (date, commit SHA, runtime, command) directly in the paper.

## Suggested Acceptance Criteria
- Paper claims map directly to current reproducible artifact tables.
- No destructive-probe terminology remains in active implementation descriptions.
- Join benchmark suite includes at least 5 shape scenarios + kernel-only slice.
- CI publishes the same benchmark artifacts used by docs.
