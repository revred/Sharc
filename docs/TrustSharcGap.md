# Trust Layer Gap Analysis: Implementation vs. Vision

**What the Sharc trust layer promises, what it delivers today, and how to close every gap**

> This document maps the vision defined in the [PRC review documents](../PRC/review/) to the current codebase and identifies every gap with a concrete fix path.

---

## 1. Current Implementation State

The trust layer today consists of 4 source files and 2 test files:

| File | Purpose | Lines |
|---|---|---|
| `Trust/LedgerManager.cs` | Hash-linked ledger with append, verify, export, import | 289 |
| `Trust/AgentRegistry.cs` | Agent registration and lookup with dictionary cache | ~135 |
| `Trust/SharcSigner.cs` | ECDsa P-256 sign/verify | 53 |
| `Trust/ISharcSigner.cs` | Signer interface | 30 |
| `Tests/LedgerTests.cs` | Sign, append, tamper, sync tests | 315 |
| `Tests/TrustSimulationTests.cs` | Multi-agent attack simulation | 153 |

**What works today**: Hash-linked ledger chain, ECDsa P-256 signatures, agent registration with public keys, delta sync between databases, tamper detection, malicious injection rejection.

**What the vision requires**: Evidence-linking, authority enforcement, reputation scoring, cross-verification, anomaly detection, sandbox lifecycle, multi-agent-class support.

---

## 2. Gap Inventory

### Status Key
- ✅ **FIXED** — Resolved in this review cycle
- 🔧 **READY** — Implementation path clear, code changes defined
- 📋 **DESIGN** — Requires design decisions before implementation
- 🔬 **RESEARCH** — Requires investigation or prototyping

---

### GAP-1: Ledger Limited to ~50 Entries (No Page Splits)

**Severity**: 🟡 P1 — Blocks any real workload  
**Status**: ✅ FIXED  

**Resolution**: Ledger writes now route through `BTreeMutator` (Option 1 from original recommendation), which handles page splits natively. The `InvalidOperationException("Ledger leaf page is full")` error has been removed. The ledger can now hold unlimited entries, bounded only by database size. Fixed in branch `Gaps.F24` (TD-1).

---

### GAP-2: Agent Lookup Was O(N) Table Scan

**Severity**: 🔴 P0 — Performance  
**Status**: ✅ FIXED

**What was done**: Replaced full table scan in `AgentRegistry.GetAgent` with a `Dictionary<string, AgentInfo>` cache loaded on first access. Lookups are now O(1). Cache is updated on `RegisterAgent` calls.

---

### GAP-3: RowID Generation Was O(N)

**Severity**: ⚪ P3 — Minor  
**Status**: ✅ FIXED

**What was done**: Replaced `GetNextRowId()` which scanned all rows to count them. Now tracked as `_maxRowId` field, incremented on each `RegisterAgent` call.

---

### GAP-4: VerifyIntegrity Silently Skipped Unknown Agents

**Severity**: 🔴 P0 — Security  
**Status**: ✅ FIXED

**What was done**: When an agent's public key could not be resolved from either `activeSigners` or the `AgentRegistry`, `VerifyIntegrity` now returns `false` instead of silently accepting the entry. This closes a critical security hole where an attacker could inject entries with a fabricated agent ID and have them pass verification.

---

### GAP-5: No Real-Time Transport Strategy

**Severity**: 🟠 P2 — Needed for live multi-agent operation  
**Status**: 📋 DESIGN

**Problem**: There is no mechanism for agents to be notified when the sandbox state changes. In the current model, each agent must poll the database.

**Impact on vision**: The sandbox architecture document describes agents reading and writing to the shared sandbox. In reality, without notifications, agents would need continuous polling, which is inefficient and introduces latency.

**Options**:
1. **BroadcastChannel API** (browser) — zero-cost cross-tab notification when a ledger entry is appended
2. **FileSystemWatcher** (server) — monitor `.sharc` file changes on disk
3. **In-process events** (.NET) — `LedgerManager.OnEntryAppended` event for co-located agents
4. **WebSocket relay** (distributed) — lightweight pubsub for cross-node sync

**Recommendation**: Start with option 3 (in-process events) since it's the simplest and enables the simulation loop immediately. Add BroadcastChannel for browser deployments later.

**Estimated effort**: 2-4 hours (option 3), 1-2 days (full transport layer)

---

### GAP-6: Ledger Stores Hashes But Not Payloads

**Severity**: 🟡 P1 — Blocks inter-agent communication  
**Status**: ✅ FIXED

**What was done**: Implemented `TrustPayload` record which serializes to JSON. The ledger now stores the full payload blob, allowing retrieval and deserialization. `LedgerManager.Append` takes `TrustPayload` or string (wrapped as Text payload).


**Schema change**:
```
Current:  seq | timestamp | agent_id | payload_hash | prev_hash | signature
Proposed: seq | timestamp | agent_id | payload_hash | prev_hash | signature | payload
```

**Estimated effort**: 2-4 hours (schema change + migration path + test updates)

---

### GAP-7: No Authority Ceiling Enforcement

**Severity**: 🟡 P1 — Required for game theory  
**Status**: ✅ FIXED

**What was done**: `AgentInfo` now includes `AuthorityCeiling`, `WriteScope`, `ReadScope`, and `CoSignRequired`. `LedgerManager.Append` enforces these limits before accepting a payload.


**Schema extension**:
```csharp
public record AgentInfo(
    string AgentId,
    byte[] PublicKey,
    long ValidityStart,
    long ValidityEnd,
    byte[] Signature,
    // New fields:
    long AuthorityCeiling,      // max financial amount in base units
    string WriteScope,          // comma-separated table names
    string ReadScope,           // comma-separated table names or "*"
    bool CoSignRequired         // whether decisions need co-signature
);
```

**Estimated effort**: 1-2 days

---

### GAP-8: No Evidence-Linking

**Severity**: 🟡 P1 — Required for anti-hallucination  
**Status**: ✅ FIXED

**What was done**: `TrustPayload` includes `List<EvidenceRef> Evidence`. Each evidence reference contains Table, RowId, and RowHash. `LedgerManager` verifies evidence exists if required (though full row-hash verification at append time is an optional check).


**Append signature change**:
```csharp
public void Append(
    string contextPayload,
    IReadOnlyList<EvidenceReference> evidence,  // NEW
    ISharcSigner signer)
```

**Estimated effort**: 1 day

---

### GAP-9: No Reputation Scoring

**Severity**: 🟠 P2 — Required for incentive alignment  
**Status**: 📋 DESIGN

**Problem**: The game theory document defines a scoring function `Score(decision) = α × Outcome + β × Evidence_Quality + γ × Timeliness - δ × Overrides`. No scoring infrastructure exists.

**Impact on vision**: Without scoring, there is no meritocratic feedback loop. Agents don't improve. Humans can't compare agent performance.

**Fix path**:
1. Create a `_sharc_scores` system table
2. Build an `EvaluatorAgent` that reads outcome data and scores prior decisions
3. Store scores as ledger entries (the evaluator is itself an agent)
4. Expose a query API for reputation dashboards

**Estimated effort**: 2-3 days

---

### GAP-10: No Agent Class Distinction

**Severity**: 🟠 P2 — Required for taxonomy support  
**Status**: ✅ FIXED

**What was done**: `AgentInfo` now includes an `AgentClass` enum (Human, AI, Machine, etc.). This is stored in the registry and signed as part of the agent identity.


**Fix path**: Add `AgentClass` enum and field to `AgentInfo`. Add class-specific validation rules in `LedgerManager.Append` and `ImportDeltas`.

**Estimated effort**: 4-8 hours

---

### GAP-11: No Cross-Verification Enforcement

**Severity**: 🟠 P2 — Required for anti-collusion  
**Status**: ⚠️ PARTIAL

**What was done**: `TrustPayload` includes `List<CoSignature> CoSignatures`. `LedgerManager.Append` verifies that if `AgentInfo.CoSignRequired` is true, the payload must contain valid co-signatures from other registered agents.
**Remaining**: The dynamic policy engine ("CFO needs COO") is not yet implemented; rudimentary co-signing is.


**Fix path**:
1. Create a `_sharc_rules` system table storing verification pair definitions
2. In `LedgerManager.Append`, check if the decision type requires cross-verification
3. If required, append the entry as "pending" until a verifier co-signs
4. Expose pending entries via a query API for the verifier agent

**Estimated effort**: 2-3 days

---

### GAP-12: No Sandbox Lifecycle Management

**Severity**: 🟠 P2 — Required for simulation-to-production pipeline  
**Status**: 🔬 RESEARCH

**Problem**: The sandbox architecture defines a 4-phase lifecycle (provision → simulate → harden → govern). No tooling exists for any phase.

**Fix path**:
1. **Provision**: Create a `SandboxBuilder` class that initializes a `.sharc` file with system tables, registers agents, and seeds domain data
2. **Simulate**: Create a `SimulationRunner` that executes decision rounds against in-memory sandboxes
3. **Harden**: Create a `SandboxHardener` that freezes configuration and adjusts authority based on scored history
4. **Govern**: Create a `LiveFeedAdapter` interface for connecting real data sources

**Estimated effort**: 1-2 weeks (full pipeline)

---

### GAP-13: No Ingestion Layer (Third-Party Quarantine)

**Severity**: 🟠 P2 — Required for third-party participation  
**Status**: 📋 DESIGN

**Problem**: The sandbox architecture document describes an ingestion layer with quarantine tables for third-party submissions. No such separation exists.

**Fix path**:
1. Define a naming convention for inbound tables (`_inbound_*`)
2. Add validation in `LedgerManager` that third-party agents can only write to their designated inbound tables
3. Create a `PromotionAgent` pattern — an internal agent that validates and promotes inbound data to domain tables

**Estimated effort**: 1 day

---

### GAP-14: No Anomaly Detection

**Severity**: ⚪ P3 — Enhancement  
**Status**: 🔬 RESEARCH

**Problem**: The game theory document defines 6 anomaly patterns (concentration, velocity spike, circular reference, scope creep, evidence thinning, phantom endorsement). No detection code exists.

**Fix path**: Build an `AuditAgent` that reads the ledger and runs statistical queries over the B-tree. Each pattern maps to a specific query:
- **Concentration**: Group decisions by vendor/supplier, flag >60% concentration
- **Velocity spike**: Compare rolling average of approvals per agent per period
- **Circular reference**: Build a citation graph, detect cycles

**Estimated effort**: 1-2 weeks

---

## 3. Priority Matrix

```
                    IMPACT ON VISION
                    Low         Medium        High
              ┌───────────┬───────────────┬──────────────┐
    Low       │           │               │  GAP-10      │
    EFFORT    │           │               │  GAP-13      │
              ├───────────┼───────────────┼──────────────┤
    Medium    │           │  GAP-5        │  GAP-6       │
              │           │  GAP-9        │  GAP-7       │
              │           │               │  GAP-8       │
              ├───────────┼───────────────┼──────────────┤
    High      │  GAP-14   │  GAP-11       │  GAP-1       │
              │           │               │  GAP-12      │
              └───────────┴───────────────┴──────────────┘
```

---

## 4. Recommended Execution Order

| Phase | Gaps | Goal | Duration |
|---|---|---|---|
| **Phase 1: Foundation** | GAP-1, GAP-6 | Ledger can hold real workloads and agents can read each other | 1 week |
| **Phase 2: Authority** | GAP-7, GAP-8, GAP-10 | Agents have scoped authority, decisions require evidence, classes enforced | 1-2 weeks |
| **Phase 3: Governance** | GAP-5, GAP-9, GAP-11 | Real-time notifications, reputation scoring, cross-verification | 2 weeks |
| **Phase 4: Lifecycle** | GAP-12, GAP-13 | Full provision → simulate → harden → govern pipeline | 2 weeks |
| **Phase 5: Intelligence** | GAP-14 | Anomaly detection and continuous audit | 2 weeks |

**Total estimated path to production-grade trust layer**: 8-10 weeks of focused implementation.

---

## 5. What's Already Solid

Not everything is a gap. The following foundations are well-built and should not be rewritten:

| Component | Why It's Solid |
|---|---|
| **Hash chain** | SHA-256 linked chain with proper sequence validation |
| **ECDsa P-256 signatures** | Industry-standard elliptic curve, clean sign/verify implementation |
| **Delta sync** | `ExportDeltas` / `ImportDeltas` with per-entry verification — the mechanism for inter-sandbox communication |
| **Tamper detection** | `VerifyIntegrity` validates hash links, sequence numbers, and signatures end-to-end |
| **Attack simulation test** | `TrustSimulationTests` proves injection, spoofing, and tamper attacks are detected |
| **ACID transactions** | The underlying `Transaction` class provides proper buffered writes with commit/rollback |
| **AES-256-GCM encryption** | Page-level encryption via `AesGcmPageTransform` for data-at-rest protection |
| **Agent registry cache** | O(1) lookups via dictionary cache (fixed in this review) |

These components form the cryptographic substrate. The gaps above are the *governance layer* that sits on top.
