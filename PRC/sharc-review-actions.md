# Sharc Critical Review — Action Tracker

> Consolidated from `CriticalReview/Latest/sharc-review-part1.md` and
> `CriticalReview/Latest/sharc-review-part1-addendum.md`.
> Last updated: 2026-02-24

---

## Legend

| Symbol | Meaning |
|--------|---------|
| DONE | Completed |
| PENDING | Not started |
| P0–P3 | Priority (P0 = blocks everything, P3 = polish) |

---

## 1. Technical Debt (TD-1 — TD-17)

*Source: Part 1, Section 3*

### 1.1 High Priority — Blocks Core Use Cases

| ID | Description | Effort | Status | Branch |
|----|-------------|--------|--------|--------|
| TD-1 | Ledger overflow at ~50 entries — route `LedgerManager.Append` through `BTreeMutator` | 4-8 h | DONE | Gaps.F24 |
| TD-2 | Row-level entitlement declared but not implemented — removed misleading API | 1 day | DONE | Gaps.F24 |
| TD-3 | `CompoundQueryExecutor` materializes full result sets — need streaming/lazy cursors | 3-5 days | PENDING | — |
| TD-4 | `ArcFileManager` trapped in WASM project — extract to platform-agnostic `Sharc.Arc` | 1 week | DONE | local.MultiCache |
| TD-5 | Encrypted write roundtrip not exercised at scale — thin integration test coverage | Medium | DONE | Add.F24 |

### 1.2 Medium Priority — Limits Adoption

| ID | Description | Effort | Status | Branch |
|----|-------------|--------|--------|--------|
| TD-6 | No RIGHT/FULL OUTER JOIN execution | 2-3 days | DONE | Gaps.F24 |
| TD-7 | No CASE expression evaluation | 1-2 days | DONE | Gaps.F24 |
| TD-8 | No append-only write fast path (sequential-insert optimisation) | 3-5 days | PENDING | — |
| TD-9 | No fuzz testing for binary parsers (B-tree, record decoder, WAL) | 3-5 days | DONE | Add.F24 |
| TD-10 | Reputation scoring formula not implemented (Bayesian model) | Medium | DONE | Gaps.F24 |
| TD-11 | No IndexedDB page source — biggest WASM adoption blocker | 1-2 weeks | PENDING | — |
| TD-12 | `BTreeMutator` 36 K monolith — refactor into composable helpers | 3-5 days | DONE | Add.F24 |

### 1.3 Low Priority — Polish & Completeness

| ID | Description | Effort | Status | Branch |
|----|-------------|--------|--------|--------|
| TD-13 | `Utf8SetContains` per-row string allocation in IN/NOT IN filter | Small | DONE | Gaps.F24 |
| TD-14 | No subquery support (`WHERE x IN (SELECT ...)`) | Large | PENDING | — |
| TD-15 | No window function execution (`OVER`, `PARTITION BY`) | Large | PENDING | — |
| TD-16 | `SecurityModel.md` says "write integrity is out of scope" — stale | 30 min | DONE | Gaps.F24 |
| TD-17 | Query pipeline split across `Sharc.Query/` and `Sharc/Query/` — documented with `DECISION:` comments | Medium | DONE | Add.F24 |

---

## 2. Foundational Library Gaps (F-1 — F-8)

*Source: Addendum, Section 1F. Critical path: F-1 → F-2 → F-3 → D-series / E-series.*

| ID | Description | Priority | Effort | Depends On | Status | Branch |
|----|-------------|----------|--------|------------|--------|--------|
| F-1 | Graph Write API (`IGraphWriter` + `GraphWriter`) | P0 | 1 week | — | DONE | Add.F24 |
| F-2 | Extended Ontology (Git + Annotation enum values) | P0 | 1 day | — | DONE | Add.F24 |
| F-3 | Cross-Arc Reference Resolution (`IArcResolver`, `arc://` URIs, ArcDiffer, ArcValidator, MCP tools) | P1 | 1-2 weeks | — | DONE | local.MultiCache |
| F-4 | Change Event Bus (`IChangeNotifier`, subscribe by `ConceptKind`) | P1 | 3-5 days | — | DONE | Add.F24 |
| F-5 | BLOB Column Codec (`CborEncoder`/`CborDecoder` + `SharcCbor` public API) | P1 | 3-5 days | — | DONE | local.MultiCache |
| F-6 | Temporal Range Queries (`FilterStar.Between()`) | P2 | 1-2 days | — | DONE | *(already existed)* |
| F-7 | Cursor-Based Pagination (`.AfterRowId()`, `.FromSequence()`) | P2 | 2-3 days | — | DONE | Add.F24 |
| F-8 | Edge History Table (`_relations_history` read/write) | P2 | 2-3 days | F-1 | DONE | local.MultiCache |

---

## 3. Graph Gaps (G-1 — G-7)

*Source: Addendum, Section 1A*

| ID | Description | Effort | Depends On | Status | Branch |
|----|-------------|--------|------------|--------|--------|
| G-1 | Graph write API (= F-1) | 3-5 days | — | DONE | Add.F24 |
| G-2 | ShortestPath algorithm (bidirectional BFS) | 2-3 days | — | DONE | local.MultiCache |
| G-3 | `GetContext()` with token budgeting → `ContextSummary` | 2-3 days | — | DONE | Add.F24 |
| G-4 | Graph-aware query integration (SQL + Traverse in one pipeline) | 1-2 weeks | — | PENDING | — |
| G-5 | Cypher/GQL-style textual graph query syntax | 1 week | — | PENDING | — |
| G-6 | Graph algorithms beyond BFS (PageRank, centrality, topo-sort) | 1 week/algo | — | PENDING | — |
| G-7 | Edge write-through to trust ledger (provenance on graph mutation) | 1-2 days | F-1 | DONE | local.MultiCache |

---

## 4. Vector Gaps (V-1 — V-7)

*Source: Addendum, Section 1B*

| ID | Description | Effort | Depends On | Status | Branch |
|----|-------------|--------|------------|--------|--------|
| V-1 | ANN index (HNSW) for sub-linear nearest-neighbour search | 2-3 weeks | — | PENDING | — |
| V-2 | IVF-Flat / product quantization for >1 M vectors | Large | V-1 | PENDING | — |
| V-3 | Vector write convenience API (`InsertVector` extension) | 2-3 days | — | DONE | Add.F24 |
| V-4 | Incremental HNSW index updates on INSERT | Medium | V-1 | PENDING | — |
| V-5 | Trust-layer integration (`IRowAccessEvaluator` + entitlement checks) | 1-2 days | — | DONE | local.MultiCache |
| V-6 | Hybrid search (vector + keyword, reciprocal rank fusion) | Medium | — | PENDING | — |
| V-7 | Dimensionality validation at write time | 1-2 days | — | DONE | local.MultiCache |

---

## 5. Compact Format Gaps (C-1 — C-4)

*Source: Addendum, Section 1C*

| ID | Description | Effort | Depends On | Status | Branch |
|----|-------------|--------|------------|--------|--------|
| C-1 | `ColumnCodec` abstraction for BLOB ↔ object mapping | See C-2 | — | DONE | local.MultiCache |
| C-2 | CBOR codec (hand-rolled, zero-dep `CborEncoder`/`CborDecoder` in Sharc.Core) | 3-5 days each | C-1 | DONE | local.MultiCache |
| C-3 | Selective field extraction (`SkipValue` skip-scan in `CborDecoder`) | Part of C-2 | C-1, C-2 | DONE | local.MultiCache |
| C-4 | Graph layer integration — lazy decode of `GraphRecord.Data` from CBOR | Medium | C-1, C-2, F-1 | DONE | Add.F24 |

---

## 6. CLI Conversation Archive Gaps (D-1 — D-3)

*Source: Addendum, Section 1D. Total estimated effort: 6-8 weeks.*

| ID | Description | Effort | Depends On | Status | Branch |
|----|-------------|--------|------------|--------|--------|
| D-1 | `sharc archive` CLI — conversation + annotation schema, capture, annotate, review commands | 3-5 days (schema + archive); 2-3 days (annotate); 3-5 days (checkpoint); 3-5 days (revert engine) | F-1, F-2, F-5 | DONE | local.MultiCache |
| D-2 | Fragment awareness & sync protocol (`_sharc_manifest`, delta export/import, conflict detection) | 1 week | F-3 | DONE | local.MultiCache |
| D-3 | MCP integration (6 tools: ArchiveConversation, SearchConversations, GetAnnotationsForFile, GetRevertInstructions, GetDecisionHistory, ApplyRevert) | 1 week | D-1, D-2 | PENDING | — |

---

## 7. Git History / GitLens Gaps (E-1 — E-7)

*Source: Addendum, Section 1E. Total estimated effort: 8-10 weeks.*

| ID | Description | Effort | Depends On | Status | Branch |
|----|-------------|--------|------------|--------|--------|
| E-1 | Enhanced schema (authors, commit_parents, branches, tags, diff_hunks, blame_lines, etc.) | 3-5 days | — | DONE | Add.F24 |
| E-2 | Migrate `CommitWriter` from `Microsoft.Data.Sqlite` to `SharcWriter` | 2-3 days | — | DONE | Add.F24 |
| E-3 | Incremental updates (skip already-indexed commits via `_index_state`) | Part of E-1 | E-1 | PENDING | — |
| E-4 | Graph overlay — map git objects to ConceptKind/RelationKind values | 3-5 days | F-1, F-2 | PENDING | — |
| E-5 | Blame data — per-line `blame_lines` table | 3-5 days | E-1 | PENDING | — |
| E-6 | Diff content — `diff_hunks` table with CBOR-encoded content | 2-3 days | E-1, F-5 | PENDING | — |
| E-7 | Link commits to conversation sessions (`session_id`/`turn_id` FKs, `ProducedBy` edge) | Medium | F-3, D-1 | PENDING | — |

---

## Summary

| Category | Total | Done | Pending |
|----------|-------|------|---------|
| Technical Debt (TD) | 17 | 13 | 4 |
| Foundational (F) | 8 | 8 | 0 |
| Graph (G) | 7 | 4 | 3 |
| Vector (V) | 7 | 3 | 4 |
| Compact Formats (C) | 4 | 4 | 0 |
| CLI Archive (D) | 3 | 2 | 1 |
| Git History (E) | 7 | 2 | 5 |
| **Total** | **53** | **36** | **17** |

*Note: G-1 and F-1 are the same functional gap (counted once in the total).*

---

## Critical Path

```
F-1 (DONE) → F-2 (DONE) → F-3 (DONE) ──→ D-1 (DONE), D-2 (DONE) + E-series in parallel
                            F-4 (DONE) ──→ G-7 (DONE)
                            F-5 (DONE) ──→ C-series (ALL DONE), D-1, E-6
                            F-7 (DONE)
E-1 (DONE) → E-2 (DONE) ──→ E-3, E-5, E-6 unblocked
```

All foundational gaps (F-1 through F-8) are now complete. E-1/E-2 are also done,
unblocking the remaining E-series items.

## Quick Wins (1-2 days each)

All quick wins completed in `local.MultiCache` branch:

- ~~F-6~~ Temporal range queries — already existed (`FilterStar.Between()`)
- ~~F-8~~ Edge history table — `GraphWriter.ArchiveEdge()` + history write on Unlink/Remove
- ~~G-7~~ Edge write-through to trust ledger — optional `LedgerManager` + `ISharcSigner` on GraphWriter
- ~~V-5~~ Trust-layer integration — `IRowAccessEvaluator` interface + zero-cost hook in `ProcessRow()`
- ~~V-7~~ Dimensionality validation — `ProbeExistingDimensions()` in `InsertVector()`
- ~~G-2~~ ShortestPath — bidirectional BFS with `MaxDepth`, `Kind`, `MinWeight`, `Timeout` support

## Wave 6 Completions (Add.F24 branch, commit 474bf25)

9 items closed in a single wave:

- ~~TD-5~~ Encrypted write roundtrip tests (12 tests)
- ~~TD-9~~ Fuzz testing for binary parsers (6 files, 42 tests)
- ~~TD-12~~ BTreeMutator → BTreePageRewriter + OverflowChainWriter
- ~~TD-17~~ Query pipeline split documented with `DECISION:` comments
- ~~E-1~~ Enhanced git schema (7 new tables, 47 tests)
- ~~E-2~~ CommitWriter migrated from Microsoft.Data.Sqlite to SharcWriter
- ~~F-4~~ Change Event Bus (IChangeNotifier + ChangeEventBus, 8 tests)
- ~~F-7~~ Cursor-based pagination (SharcDataReader.AfterRowId(), 8 tests)
- ~~C-4~~ Lazy CBOR decode in GraphRecord (RawCborData property, 6 tests)

## Remaining 17 Items

| ID | Description | Category |
| ---- | ------------- | ---------- |
| TD-3 | CompoundQueryExecutor streaming | High TD |
| TD-8 | Sequential-insert fast path | Medium TD |
| TD-11 | IndexedDB page source (WASM) | Medium TD |
| TD-14 | Subquery support | Low TD |
| TD-15 | Window functions | Low TD |
| G-4 | Graph-aware query (SQL + Traverse) | Graph |
| G-5 | Cypher/GQL syntax | Graph |
| G-6 | Graph algorithms (PageRank, centrality) | Graph |
| V-1 | HNSW ANN index | Vector |
| V-2 | IVF-Flat / product quantization | Vector (depends V-1) |
| V-4 | Incremental HNSW updates | Vector (depends V-1) |
| V-6 | Hybrid search / RRF | Vector |
| D-3 | MCP Archive Tools (6 tools) | CLI Archive |
| E-3 | Incremental git updates | Git (depends E-1 ✓) |
| E-4 | Graph overlay for git objects | Git |
| E-5 | Blame data | Git (depends E-1 ✓) |
| E-6 | Diff content with CBOR hunks | Git (depends E-1 ✓) |
| E-7 | Link commits to conversations | Git |
