# V-Series: Hot-Path Optimization TODO

## Completed (WP1-WP9, Round 1)

- [x] **V-01** HNSW SortedSet/HashSet → CandidateHeap/VisitedSet (WP1)
- [x] **V-02** SeekIndex → SeekFirst + bounded prefix scan (WP2)
- [x] **V-03** HybridQuery text full-sort → bounded heap (WP3)
- [x] **V-04** Trust blob GetBlob → GetBlobSpan / remove .ToArray() (WP4)
- [x] **V-05** ShadowPageSource zero-copy dirty page Memory (WP5)
- [x] **V-06** ExecutionRouter param key canonicalization (WP6)
- [x] **V-07** StreamingAggregator precomputed output map (WP7)
- [x] **V-08** HNSW builder CollectionsMarshal + pooled arrays (WP8)
- [x] **V-09** RowIds LINQ → cached array (WP9)

## Completed (Round 2)

- [x] **V-11** Join path ORDER BY+LIMIT → TopN heap (ApplyOrderByTopN)
- [x] **V-12** Compound query ORDER BY+LIMIT → TopN heap (shared impl)
- [x] **V-13** CASE column-name resolution → pre-bound ordinal cache (AST walker)
- [x] **V-16** Projection resolution LINQ → GetColumnOrdinal
- [x] **V-17** Audit verification → IncrementalHash + GetBlobSpan (span-based)
- [x] **V-18** CBOR selective field → UTF-8 MatchTextString (zero-alloc key compare)
- [x] **V-19** SharcDataReader.GetOrdinal → lazy Dictionary cache
- [x] **V-20** Graph BFS ContainsKey+indexer → TryAdd
- [x] **V-21** Graph store init FirstOrDefault → GetColumnOrdinal + explicit loops

## Completed (W-Series: Point Lookup, Scan, Filter)

- [x] **W-01** SeekIndex UTF-8 string key → stackalloc (zero-alloc for ≤256 B)
- [x] **W-02** SeekIndex full DecodeRecord → TryDecodeIndexRecord (first-N keys + trailing rowid)
- [x] **W-03** Composite index multi-column prefix planning (score × prefix count)
- [x] **W-05** ResolvedFilter[] per-filter offset walk → precomputed offsets (stackalloc)
- [x] **W-06** FilterStarCompiler AND children → selectivity-ordered (cheap predicates first)

## Already Done (discovered during W-Series)

- [x] **W-07** PreparedQuery param cache key — already unified via ParameterKeyHasher
- [x] **W-08** Filter compilation FirstOrDefault — already replaced with BuildColumnMap + ResolveColumn

## Skipped

- [ ] **W-04** Bounded max-ordinal for ComputeColumnOffsets — skipped: public API allows any ordinal access via GetString/GetInt64, bounding offsets risks stale data for columns beyond bound

---

## Remaining — Deferred

### V-10: SeekIndex per-row record decoding elimination
**Status:** Deferred — marginal gain after V-02 (bounded prefix range is typically 5-50 rows).

### V-14: Trust ledger signing .ToArray()
**Status:** Deferred — requires ISharcSigner public API change (`SignAsync(byte[])` → `SignAsync(ReadOnlyMemory<byte>)`). Breaking change candidate for next major version.

### V-15: ShadowPageSource dirty read copy
**Status:** Done (V-05). Remaining caller audit found no additional ToArray paths.

### V-22: Git walker string allocation
**Status:** Deferred — tooling code, not library hot path. Low priority.
