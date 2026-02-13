# Architecture: Distributed Trust & Synchronization

This document proposes technical solutions to the "Hard Problems" of decentralized key management and multi-master synchronization within the Sharc Context Engine. More fundamentally, it articulates **why** solving these problems is not merely an engineering exercise, but a prerequisite for the next era of artificial intelligence.

---

## 0. The Thesis: Trust Is the Missing Layer

Today's AI agents are **stateless oracles**. They receive a prompt, generate a response, and forget. Their "memory" is a context window — a volatile, ephemeral scratchpad that evaporates the moment a session ends.

This is the fundamental bottleneck. Not model size. Not inference speed. **The inability of agents to form persistent, verifiable, shared memory.**

Consider what happens when you ask three different AI models to research a topic:
- **Gemini** finds a key paper and summarizes it.
- **Claude** discovers a contradicting dataset.
- **Codex** writes code that depends on the first paper's conclusions.

Today, these three insights exist in three separate, unreachable silos. There is no mechanism for Codex to *know* that Claude found contradicting evidence. There is no way for a fourth agent to verify *who said what*, or whether the data has been tampered with since it was first recorded.

**Sharc solves this.** Not by building another chat protocol, but by providing a **cryptographically verifiable, ACID-compliant, binary-efficient shared memory** that any agent can read, write to, and *trust*.

> The problem is not "how do we get agents to talk to each other."
> The problem is: **how do we get agents to *remember together* in a way that is provably correct.**

---

## 1. Decentralized Key Management (B-Tree PKI)

To move beyond manual key passing, Sharc implements a native **Agent Registry** using its existing B-tree layer.

### A. The `_sharc_agents` System Table
A reserved table that acts as a local Public Key Infrastructure (PKI).
- **Schema**: `(AgentId TEXT PRIMARY KEY, PublicKey BLOB, ValidityStart INTEGER, ValidityEnd INTEGER, Signature BLOB)`
- **Self-Attestation**: Agents self-register by signing their own registration entry.
- **Root of Trust**: The first entry can be anchored to an external source (e.g., a GitHub commit signature or a DID document).

### B. DID Integration
- Use **Decentralized Identifiers (DIDs)** for `AgentId`.
- A `SharcDidResolver` resolves keys from the local `_sharc_agents` table or external providers (`did:key`, `did:web`).

### C. Key Rotation via Ledger
Key rotation is a standard Ledger entry:
1. Agent appends a `KeyRotation` payload, signed by the **old** key.
2. Once verified, `_sharc_agents` is updated with the **new** key.
3. The chain of trust is continuous and auditable.

---

## 2. Multi-Master Synchronization (Ledger-Based CRDT)

Sharc avoids merging binary B-tree pages by synchronizing at the **Operation Level** using the Ledger.

### A. The Ledger as a Causal Stream
Each entry in `_sharc_ledger` is a deterministic state change.
- **Forks**: Duplicate `SequenceNumber` with different `PayloadHash` = fork detected.
- **Resolution**: **Deterministic Fork Resolution (DFR)** — lower lexicographical SHA-256 hash wins. The losing branch is preserved in `_sharc_forks` for reconciliation.

### B. Delta Replication
Agents exchange **Ledger Deltas**, not whole files:
1. Agent A sends `LastKnownSequence = 500`.
2. Agent B streams entries `501` to `N` as raw binary records.
3. Agent A appends, verifies signatures, and is in sync.

### C. Conflict-Free Replicated Data Types (CRDT)
Sharc tables can implement **LWW-Element-Set** semantics:
- Hidden `_sharc_version` column (timestamp + agent_id).
- On merge, highest version wins per Primary Key.

---

## 3. Why This Matters: The Paradigm Shift

### A. From Stateless Agents to Stateful Collectives

Current AI architectures treat each model invocation as an isolated event. But intelligence is not isolated. Human intelligence is deeply **social** — it is built on shared knowledge, accumulated evidence, and the ability to *trust* the provenance of information.

Sharc enables models to form **Stateful Collectives**: groups of agents that share a persistent, append-only, cryptographically-attributed knowledge base. This is not "tool use." This is **shared cognition**.

```
┌─────────────────────────────────────────────────────┐
│              Sharc Context Space (.sharc)            │
│                                                     │
│  _sharc_ledger   (What happened, in what order)     │
│  _sharc_agents   (Who is trusted to contribute)     │
│  oncology_trials (The actual shared knowledge)      │
│  genomic_variants(Domain-specific data tables)      │
│                                                     │
│  Every row: signed, sequenced, hash-linked.         │
│  Every reader: gets JIT-filtered, binary-efficient  │
│  slices of exactly what they need.                  │
└─────────────────────────────────────────────────────┘
         ▲              ▲              ▲
         │              │              │
     ┌───┴───┐    ┌─────┴─────┐   ┌───┴───┐
     │ Alice │    │    Bob    │   │ Claude│
     │(Gemini│    │(Codex CLI)│   │ (API) │
     └───────┘    └───────────┘   └───────┘
```

### B. Trust as an Alignment Primitive

Today, AI alignment is pursued through RLHF, constitutional AI, and similar training-time techniques. These are necessary but insufficient. They address **what a model says** but not **what a model knows**.

Consider a scenario where a model's training data contains a subtle error about a drug interaction. If that model writes its finding to a shared Sharc database:
1. The entry is **signed** — we know *who* contributed it.
2. The entry is **hash-linked** — we know *when* it was added, and that nothing before it was altered.
3. A second model can **challenge** it by appending a contradicting entry, also signed.
4. A human reviewer can audit the entire chain: *who said what, when, and whether the chain is intact*.

This is **post-hoc alignment through verifiable provenance**. It does not prevent a model from being wrong, but it makes it impossible to be wrong *silently* or *anonymously*.

### C. The Economics of Frugal Intelligence

MCP and similar protocols assume that intelligence is centralized — a model behind an API endpoint that you query. Sharc assumes the opposite: **intelligence is distributed, and context is the scarce resource**.

A `.sharc` file on GitHub is:
- **Free to host** (Git LFS or standard binary).
- **Free to query** (no API keys, no rate limits, no server).
- **Incrementally growable** (agents append; they don't need to rewrite).
- **Universally portable** (SQLite-compatible binary format).

This means a global oncology knowledge base, maintained by dozens of AI agents across different organizations, can exist as a **single file in a repository** — versioned, branched, merged, and audited using the same tools developers already use.

### D. The Safety Argument

The most dangerous AI is an AI you cannot audit. Current systems produce outputs but leave no trace of the reasoning that led to those outputs. Sharc's ledger inverts this:

| Property | Current AI | Sharc-Enabled AI |
|---|---|---|
| **Memory** | Ephemeral (context window) | Persistent (B-tree) |
| **Attribution** | Anonymous | Cryptographically signed |
| **Auditability** | None | Full chain verification |
| **Collaboration** | Isolated silos | Shared, synchronized state |
| **Tamper Evidence** | None | SHA-256 hash chain |

> An AI system without verifiable memory is a system that cannot be held accountable.
> Accountability requires provenance, and provenance requires a ledger.
> This is not optional for safe AI at scale.

### E. The Multi-Agent Trust Boundary

The most immediate practical problem this architecture solves is the **trust boundary** in multi-agent systems. When Agent A delegates a sub-task to Agent B, and Agent B returns a result, how does Agent A — or the human operator — know:

1. That Agent B actually performed the work (not a man-in-the-middle)?
2. That the result hasn't been modified in transit?
3. That Agent B had the authority to perform that action?
4. That a complete audit trail exists for regulatory compliance?

Without Sharc, these questions require centralized orchestration, API keys, and blind trust in transport layers. With Sharc, the answers are embedded in the data itself:

1. **Authenticity**: Every entry is signed by the agent's ECDsa P-256 key.
2. **Integrity**: Every entry is hash-linked to its predecessor. Tampering breaks the chain.
3. **Authorization**: The `_sharc_agents` registry defines who is trusted. Unknown agents are rejected at import time.
4. **Auditability**: The `_sharc_ledger` is an immutable, append-only log. It *is* the audit trail.

### F. The Convergence: Context Space Engineering

We propose the term **Context Space Engineering (CSE)** for this discipline. CSE is the practice of designing, populating, and securing the shared memory substrates upon which multi-agent intelligence operates.

CSE is to AI what database design is to software engineering — the discipline that determines whether the system is correct, performant, and trustworthy. Without it, agents build on sand. With it, they build on bedrock.

The core primitives of CSE are:
- **Context Spaces**: Binary-efficient, schema-typed shared memory (Sharc).
- **Trust Chains**: Cryptographic attribution and integrity verification (Ledger + Agents).
- **Synchronization Protocols**: Delta replication with signature verification.
- **Entitlement Boundaries**: Row-level security that ensures agents see only what they should.
- **Conflict Resolution**: Deterministic strategies for reconciling concurrent contributions.

---

## 4. Road Map

| Phase | Component | Status |
|-------|-----------|--------|
| C1 | Distributed Ledger (B-tree) | ✅ Complete |
| C3 | Agent Registry (`_sharc_agents`) | ✅ Complete |
| C5 | Cryptographic Attribution (ECDsa P-256) | ✅ Complete |
| C6 | Delta Replication (Export/Import) | ✅ Complete |
| C7 | Multi-Bot Trust Simulation | 🔄 In Progress |
| C4 | Row-Level Entitlement (RLE) | ⬜ Planned |
| D1 | Native CRDT merge support | ⬜ Planned |
| D2 | Cross-language SDK (Python, TypeScript) | ⬜ Planned |

---

## 5. Summary

By treating the **Ledger as the Source of Truth** and the **B-tree as a Cache of the Current State**, Sharc solves the multi-master problem. But more than that, it provides something no existing protocol offers: **a substrate for collective AI memory that is persistent, efficient, attributable, and tamper-evident**.

Trust is not a session. Trust is not a token. Trust is the verifiable history of operations signed by agents whose identities are locally and globally resolvable. **Trust is data.**

The future of AI is not a single, omniscient model. It is a network of specialized agents that **remember together** — and Sharc is the medium through which they remember.
