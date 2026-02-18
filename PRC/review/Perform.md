# Total Memory Allocation and Performance Review

## Findings (ordered by severity)

1. **[High] JOIN path is fully materialized, wide, and late-filtered**
   - Evidence: `src/Sharc/Query/Execution/JoinExecutor.cs:22`, `src/Sharc/Query/Execution/JoinExecutor.cs:37`, `src/Sharc/Query/Execution/JoinExecutor.cs:45`, `src/Sharc/Query/Execution/JoinExecutor.cs:186`, `src/Sharc/Query/Execution/JoinExecutor.cs:229`
   - Impact: O(left + right + result) memory, plus copying all projected/unprojected values before filtering. This is the biggest allocation and latency risk for large joins.
   - Recommendation: move to a pipelined join operator with projection/filter pushdown (only join keys + needed output/filter columns), and choose build/probe side by estimated cardinality.

2. **[High] View resolution recompiles view SQL per query execution**
   - Evidence: `src/Sharc/SharcDatabase.cs:324`, `src/Sharc/SharcDatabase.cs:328`, `src/Sharc/SharcDatabase.cs:399`, `src/Sharc/SharcDatabase.cs:413`
   - Impact: repeated parse/compile/topological-sort allocations despite query plan caching. Cost grows with nested views.
   - Recommendation: cache compiled view plans and resolved view-expanded plans keyed by `(queryText, schemaCookie)`.

3. **[High] Trust/ledger path performs repeated blob copies and crypto buffer allocations**
   - Evidence: `src/Sharc/Trust/LedgerManager.cs:156`, `src/Sharc/Trust/LedgerManager.cs:157`, `src/Sharc/Trust/LedgerManager.cs:158`, `src/Sharc/Trust/LedgerManager.cs:159`, `src/Sharc/Trust/LedgerManager.cs:297`, `src/Sharc/Trust/LedgerManager.cs:328`, `src/Sharc/Trust/SharcSigner.cs:32`, `src/Sharc/Trust/SharcSigner.cs:38`, `src/Sharc/Trust/SharcSigner.cs:55`
   - Impact: avoidable GC pressure during verification/import/export; scales poorly with large ledgers.
   - Recommendation: prefer span-based paths (`GetBlobSpan`, span hash/sign APIs, `TryComputeHash`) and avoid `ToArray()` in hot loops.

4. **[Medium] Entitlement scopes are parsed repeatedly**
   - Evidence: `src/Sharc/Trust/EntitlementEnforcer.cs:26`, `src/Sharc/Trust/EntitlementEnforcer.cs:37`, `src/Sharc/Trust/EntitlementEnforcer.cs:48`, `src/Sharc/Trust/ScopeDescriptor.cs:41`
   - Impact: repeated string/list allocations and linear scans per request for same agent/scope.
   - Recommendation: cache parsed scope descriptors per scope string (or per agent snapshot), then reuse immutable lookup structures.

5. **[Medium] Materialized fingerprinting allocates UTF-8 byte arrays per text value**
   - Evidence: `src/Sharc/SharcDataReader.cs:1013`, `src/Sharc/SharcDataReader.cs:1047`
   - Impact: fallback/materialized paths lose much of zero-allocation behavior under set ops/aggregation.
   - Recommendation: hash text without allocating intermediary byte arrays (direct ordinal text hashing or pooled encoder buffer).

6. **[Medium] `GetUtf8Span` allocates in materialized mode**
   - Evidence: `src/Sharc/SharcDataReader.cs:858`
   - Impact: API looks zero-copy but allocates in this mode, creating surprise and GC churn.
   - Recommendation: either cache UTF-8 per value or expose this behavior explicitly with a separate API contract.

7. **[Medium] Parameter filter cache key is order-sensitive**
   - Evidence: `src/Sharc/SharcDatabase.cs:562`, `src/Sharc/SharcDatabase.cs:570`
   - Impact: equivalent parameter sets can miss cache depending on dictionary enumeration order, increasing compile churn.
   - Recommendation: canonicalize key generation (sorted keys) and consider bounded cache policy.

8. **[Low] `ViewSqlScanner` uses `Substring(...).Trim()`**
   - Evidence: `src/Sharc.Query/Sharq/ViewSqlScanner.cs:55`
   - Impact: extra string allocation and avoidable work on a frequent path.
   - Recommendation: trim by span indices and allocate once.

9. **[Testing gap] No allocation benchmarks for JOIN/VIEW/entitlement hot paths**
   - Evidence: correctness-only join tests at `tests/Sharc.Tests/Query/JoinTests.cs:50`; allocation tests are simple scans at `tests/Sharc.IntegrationTests/QueryRobustnessTests.cs:308`; query benchmarks focus on set ops/Cote, not JOIN/VIEW/entitlement (`bench/Sharc.Comparisons/QueryRoundtripBenchmarks.cs:224`, `bench/Sharc.Comparisons/QueryRoundtripBenchmarks.cs:402`).
   - Recommendation: add benchmark + allocation budgets specifically for JOIN, nested VIEW, and entitlement enforcement.

## Architecture-level strategy (complexity down, readability up)

1. Introduce a small physical operator pipeline (`Scan -> Filter -> Project -> Join -> PostProcess`) to remove duplicated materialize/filter logic and improve readability.
2. Add two explicit caches: `ViewExpansionCache` and `ScopeDescriptorCache` with invalidation on schema/agent changes.
3. Make trust/ledger internals span-first and copy-late to keep code simple and memory-stable.

## Tactical near-term wins

1. Refactor `JoinExecutor` to early-project + early-filter before row merge.
2. Add view expansion cache keyed by `schemaCookie`.
3. Replace hot `ToArray()` in ledger verification/export with span-friendly APIs.
4. Add `ConcurrentDictionary<string, ScopeDescriptor>` cache in entitlement enforcement.
5. Remove `Substring(...).Trim()` in view scanner in favor of span slicing.

## Suggested benchmark additions

1. JOIN benchmark: 10K x 10K with selective predicates (inner + left).
2. Nested VIEW benchmark: 3-5 levels of dependent views.
3. Entitlement benchmark: repeated enforcement on same agent/scope.
4. Ledger benchmark: integrity verification over 10K entries.

## Success criteria

1. Lower peak memory for JOIN and VIEW paths.
2. Reduced per-query allocations on repeated workloads.
3. Stable throughput under sustained ledger verification and entitlement checks.
4. Simpler execution architecture with fewer one-off materialization branches.
