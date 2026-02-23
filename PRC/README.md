# PRC (Process, Requests, Comments)

This directory contains **Process, Requests, and Comments** — the engineering log and design repository for Sharc.

> **Note**: Documents here are **snapshots in time**. They represent design intent, architectural decisions, and execution plans. Unlike `docs/`, which reflects the *current* state of user-facing documentation, `PRC/` contains specifications, action plans, and historical context.

---

## Core Reference (Always Current)

| Document | Purpose |
|:---------|:--------|
| [**ArchitectureOverview.md**](ArchitectureOverview.md) | The deep engineering manual — layer-by-layer system design |
| [**DecisionLog.md**](DecisionLog.md) | "Why did we do X?" — History of all architectural decisions |
| [**PerformanceBaseline.md**](PerformanceBaseline.md) | Current benchmark numbers, allocation tiers, Sharc vs SQLite |
| [**BenchmarkWorkflow.md**](BenchmarkWorkflow.md) | How to run benchmarks — chunk definitions, filter patterns, protocol |
| [**DependencyPolicy.md**](DependencyPolicy.md) | 7-criteria evaluation for any new dependency (zero-dep policy) |
| [**RecallIdentityPolicy.md**](RecallIdentityPolicy.md) | Tiered identity model — when to use rowid vs fingerprint vs GUID |

## Specifications (Load-Bearing Design Docs)

| Document | Status | Purpose |
|:---------|:-------|:--------|
| [**JitSQL.md**](JitSQL.md) | Active | JitSQL specification — execution hints, view support, PreparedTraversal |
| [**EncryptionSpec.md**](EncryptionSpec.md) | Active | AES-256-GCM page encryption with Argon2id KDF |
| [**WALDesign.md**](WALDesign.md) | Active | Write-Ahead Log read-only support design |
| [**SecurityModel.md**](SecurityModel.md) | Active | Threat model, encryption, parser hardening, memory safety |
| [**ErrorHandling.md**](ErrorHandling.md) | Active | Exception hierarchy and ThrowHelper patterns |
| [**GraphSupport.md**](GraphSupport.md) | Active | Graph engine design — context compression, integer-first addressing |
| [**CompatibilityMatrix.md**](CompatibilityMatrix.md) | Active | SQLite feature support status matrix |
| [**LayerAsView.md**](LayerAsView.md) | Active | Views as reusable cursor configurations (ILayer interface) |
| [**Joins.md**](Joins.md) | Active | Join implementation — hash join, nested loop, index seek |
| [**Views.md**](Views.md) | Active | View resolution and sqlite_schema integration |

## Optimization Specs (Implemented)

| Document | Status | Purpose |
|:---------|:-------|:--------|
| [**HotScanMode.md**](HotScanMode.md) | Implemented | Branchless Read() dispatch via ScanMode enum + jump table |
| [**CursorUnionOptimization.md**](CursorUnionOptimization.md) | Implemented | Polymorphic cursor projection — PointLookup 430 ns to 108 ns |
| [**PointLookup100x.md**](PointLookup100x.md) | Implemented | Cold path from 92x to 199x vs SQLite |
| [**PreparedInterfaces.md**](PreparedInterfaces.md) | Implemented | IPreparedReader/IPreparedWriter, ThreadLocal template pattern |
| [**PlanLocalCache.md**](PlanLocalCache.md) | Implemented | Plan cache for query compilation |

## Strategy and Methodology

| Document | Purpose |
|:---------|:--------|
| [**StrategyDecision.md**](StrategyDecision.md) | Why pure managed C# (no P/Invoke, no native deps) |
| [**PerformanceStrategy.md**](PerformanceStrategy.md) | Performance goals, zero-alloc design, ADR references |
| [**BenchmarkProtocol.md**](BenchmarkProtocol.md) | Run-analyze-optimize loop, allocation tiers T0-T5 |
| [**BenchmarkSpec.md**](BenchmarkSpec.md) | Fair Sharc vs SQLite benchmark design — 10 categories |
| [**TestStrategy.md**](TestStrategy.md) | Testing philosophy — inference-based, TDD workflow |
| [**DataStructureTests.md**](DataStructureTests.md) | Test plan for core readonly structs and decoders |
| [**CommitChecklist.md**](CommitChecklist.md) | Pre-commit verification steps |
| [**Glossary.md**](Glossary.md) | Domain terminology reference |

## JitSQL and Stored Procedures (Design Phase)

| Document | Status | Purpose |
|:---------|:-------|:--------|
| [**JitConceptStatement.md**](JitConceptStatement.md) | Design | JIT compilation concept and architecture |
| [**PotentialJitInternalUse.md**](PotentialJitInternalUse.md) | Design | Internal optimization opportunities for JIT |
| [**WildUserScenariosForJitUse.md**](WildUserScenariosForJitUse.md) | Design | User-facing JIT scenarios and use cases |
| [**PlanStoredProcedures.md**](PlanStoredProcedures.md) | Design | Stored procedure execution model |
| [**PlanCoreStoredProcedures.md**](PlanCoreStoredProcedures.md) | Design | Core engine stored procedure implementations |
| [**PlanGraphStoredProcedures.md**](PlanGraphStoredProcedures.md) | Design | Graph-specific stored procedures |
| [**PlanVectorBuildingBlocks.md**](PlanVectorBuildingBlocks.md) | Design | Vector/embedding storage building blocks |

## Action Plans (Execution Tracking)

| Document | Status | Purpose |
|:---------|:-------|:--------|
| [**ExecutionPlan.md**](ExecutionPlan.md) | M1-M14 complete | High-level roadmap — needs M15+ for query pipeline |
| [**FilterActionPlan.md**](FilterActionPlan.md) | Superseded by FilterStar/BakedFilter | Three-phase filter engine redesign |
| [**WriteActionPlan.md**](WriteActionPlan.md) | Phase 1 complete | Write engine implementation — INSERT/UPDATE/DELETE |

## Benchmark Reports (Point-in-Time)

| Document | Date | Purpose |
|:---------|:-----|:--------|
| [**ExecutionTierReport.md**](ExecutionTierReport.md) | 2026-02-22 | DIRECT vs CACHED vs JIT tier benchmarks |
| [**QueryPipelineOverview.md**](QueryPipelineOverview.md) | 2026-02-22 | Query pipeline architecture and performance |

## Archive (Historical)

These documents remain in the repo for historical context but are no longer authoritative. Read for background understanding only.

| Document | Why Archived |
|:---------|:-------------|
| [**StatusReportPhase2.md**](StatusReportPhase2.md) | Phase 2 milestone snapshot (1,064 tests) — superseded by current 2,660+ tests |
| [**BenchGraphSpec.md**](BenchGraphSpec.md) | SurrealDB competitive benchmark spec — comparison deprioritized |
| [**ArenaUpgrade.md**](ArenaUpgrade.md) | Seven-page Arena redesign spec — not yet started, future work |

## Reviews (PRC/review/)

Internal design reviews and architectural reflections:

| Document | Purpose |
|:---------|:--------|
| [review/deepReviewPhase1.md](review/deepReviewPhase1.md) | Phase 1 deep review |
| [review/reflectionPhase2.md](review/reflectionPhase2.md) | Phase 2 reflection |
| [review/TrustLayerReview.md](review/TrustLayerReview.md) | Trust layer architecture review |
| [review/SandboxArchitecture.md](review/SandboxArchitecture.md) | Sandbox architecture evaluation |
| [review/AgentTaxonomy.md](review/AgentTaxonomy.md) | Agent classification taxonomy |
| [review/GameTheory.md](review/GameTheory.md) | Game-theoretic trust analysis |
| [review/Entitlement.md](review/Entitlement.md) | Entitlement model design |
| [review/Perform.md](review/Perform.md) | Performance review notes |
| [review/WriteLeft.md](review/WriteLeft.md) | Write engine remaining work |

## Cross-Reference

User-facing documentation lives in [`docs/`](../docs/README.md). Key internal references from docs/:

- [docs/TrustSharcGap.md](../docs/TrustSharcGap.md) — 14 identified trust layer gaps with fix status
- [docs/DistributedTrustArchitecture.md](../docs/DistributedTrustArchitecture.md) — Distributed trust architecture
- [docs/BTreeFingerPrint.md](../docs/BTreeFingerPrint.md) — Fingerprint-based dedup benchmark results
- [docs/MultiAgentAccess.md](../docs/MultiAgentAccess.md) — Multi-agent DataVersion/IsStale pattern
- [docs/UsageFriendlyErrors.md](../docs/UsageFriendlyErrors.md) — User-friendly error message catalog
