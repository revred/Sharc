# Sharc DataSet vs. Model Context Protocol (MCP)

**Curated with intent by Ram Revanur**

This document provides a strategic comparison between the **Model Context Protocol (MCP)** and **Sharc**, highlighting why Sharc represents the evolution of context management for Agentic AI.

---

## 1. The Protocol Gap: Ephemeral vs. Permanent

While MCP is a valuable standard for connecting LLMs to external tools, it operates as a **transport protocol**. Sharc operates as a **Context Engine**.

| Feature | Model Context Protocol (MCP) | Sharc Context Engine |
| :--- | :--- | :--- |
| **Philosophy** | "Bridge to Silos" | "Foundation for Context" |
| **Format** | Textual (JSON-RPC 2.0) | Binary (RecordEncoded B-trees) |
| **Structure** | Unstructured JSON blobs | **Typed Graph + Relational Tables** |
| **Latency** | High (Serialization overhead) | Ultra-Low (Direct memory access) |
| **Persistence** | Ephemeral/Session-based | Permanent/ACID-compliant |
| **Trust Source** | Connection (OAuth/Stdio) | **Cryptographic (Ledger/Registry)** |

---

## 2. Why MCP is Dated and Limited

### A. The JSON-RPC Bottleneck
MCP relies on JSON-RPC over text-based streams (Stdio or HTTP/SSE). In the era of high-frequency agent interaction, the overhead of converting structured state into JSON strings and back again is a primary bottleneck. It treats context as "messages" rather than "memory," leading to significant CPU and latency penalties during complex state sharing.

### B. Transient Trust
MCP is primarily focused on the **communication session**. It assumes that if a connection is established, the data flowing through it is trusted. However, it lacks a native, immutable record of provenance. Once a piece of context leaves an MCP server, its lineage is lost. There is no standard for verifying *exactly* which agent modified which sub-fragment of data over time to prevent "context injection" or "gaslighting" attacks.

### C. Coarse-Grained Retrieval
MCP "Resources" are typically fetched as discrete units. To prune or filter that context, the model must either ingest the entire resource (wasting tokens) or the server must implement bespoke, non-standard filtering. There is no unified mechanism for **predicate pushdown**—the ability to filter data at the storage layer before it ever reaches the network.

---

## 3. The Sharc Advantage: A New Medium for Context

Sharc was designed to solve the "Context Problem" from the storage level up, ensuring that context is not just shared, but **distributed and trusted**.

### A. Filter Star JIT (Selection Performance)
Sharc's `FilterStarCompiler` generates machine-code predicates that filter context directly in the database layer. This ensures that the AI model's context window is only populated with **relevant** fragments, dramatically reducing token costs and increasing reasoning accuracy.

### B. Distributed Ledger (Trust and Lineage)
Integrated into the hardware-efficient B-tree layer, the Sharc Ledger provides a tamper-evident audit trail (SHA-256). Every piece of context shared via Sharc is part of a verifiable cryptographic chain. Any attempt to modify historical state is detected by the `VerifyIntegrity` engine, providing a level of "Proven Integrity" that a pure communication protocol like MCP cannot offer.

### C. Cryptographic Attribution (Non-Repudiation)
Every row in Sharc can be bound to an agent's identity via **ECDsa P-256 signatures**. This establishes a definitive "Who/What/When" audit trail. Unlike MCP, where tools act on behalf of the user, Sharc records agents acting on behalf of themselves, creating a verifiable economy of context.

### D. ACID Reliability
Context in Sharc is durable. Using **Rollback Journals** and **Shadow Paging**, Sharc ensures that trust data is never in a corrupted state, even during system crashes or network interruptions. Sharc guarantees that the "Global Context" remains consistent and auditable at all times.

### E. The Graph Advantage (Structured Context)
MCP resources are typically flat files or JSON blobs. Sharc includes a native **Graph Engine** (`Sharc.Graph`) that allows agents to traverse relationships (e.g., `Paper -> cites -> Paper`) with O(log N) efficiency. This means an agent can request "all papers cited by X within 2 hops" without retrieving the entire dataset, a capability that raw MCP lacks.

---

## 4. Case Study: The Frugal Oncology Knowledge Base

To illustrate the practical superiority of Sharc over MCP for distributed research, consider the creation of a global **Oncology Knowledge Base** hosted on GitHub.

### A. Frugal and Portable Storage
Traditional databases or large JSON collections are too bloated for frequent GitHub synchronization. Sharc’s single-file, binary B-tree format allows for a multi-gigabyte oncology dataset (clinical trials, genomic variants, patient cohorts) to be represented with extreme space efficiency. The database is the repository.

### B. Streamable Selection (On-Demand Intelligence)
In an MCP-based system, a model like **Gemini** or **Claude** might have to request a large "ClinicalTrials" resource and then parse it. With Sharc, the model sends a **JIT Filter Star** query. 
- *Query*: `SELECT * FROM trials WHERE phase = 3 AND primary_site = 'Lung' AND mutation = 'EGFR'`
- *Result*: Sharc streams only the matching binary rows. This ensures the model's context window is filled with high-signal data, not noise.

### C. Incremental, Credible Population
The database is not static. It grows incrementally as diverse CLIs (Codex, GPT, Claude) contribute new insights:
1. **Incremental Writes**: When a model identifies a new correlation in a research paper, it appends an entry via the `LedgerManager`.
2. **Cryptographic Attribution**: The entry is signed by the model's unique Agent ID. This creates a **Trust Ledger** where every medical insight has a verifiable origin.
3. **Collaboration without Collision**: Sharc’s ACID compliance ensures that multiple agents can contribute to the GitHub-hosted database simultaneously (via branches or synchronized WALs) without corrupting the knowledge state.

---

## 5. Summary: The Medium of Choice

---

## 6. Devil’s Advocate: Gaps, Gaps, and Grandstanding

While the vision for Sharc is compelling, a rigorous critique reveals significant gaps between the current "Agent Trust" implementation and the "Grand Standing" claims of replacing MCP.

### A. Conceptual Gaps: The "Ecosystem" vs. The "Engine"
- **Interoperability Paradox**: MCP’s JSON-RPC is slow, but it is **universal**. Any language can implement an MCP server in hours. Sharc requires a deep, binary-aware implementation of the SQLite B-tree format. By moving context to Sharc, we risk trading "Protocol Bloat" for "Implementation Isolation."
- **Discovery vs. Delivery**: MCP provides a standardized way for agents to *discover* tools (ListTools). Sharc provides the *data delivery* (Ledger/B-tree). Currently, Sharc lacks a standardized way for a new agent to arrive at a database and "know" what context domains are available without out-of-band schema documentation.
- **The "Distributed" Illusion**: The document claims a "Distributed Ledger," but the current implementation is **Local-First**. Synchronizing B-trees across GitHub (as mentioned in the Oncology case study) is prone to binary merge conflicts. True distribution requires a consensus or merging layer (CRDT or similar) which is absent from the core engine.

### B. Implementation Gaps: Code Realities
- **Discovery vs. Delivery**: MCP provides a standardized way for agents to *discover* tools (ListTools). Sharc provides the *data delivery* (Ledger/B-tree). Currently, Sharc lacks a standardized way for a new agent to arrive at a database and "know" what context domains are available without out-of-band schema documentation.
- **Contention and Locking**: Direct B-tree appends are fast, but they assume low concurrency. In a multi-agent CLI environment (Gemini, Claude, and Codex interacting simultaneously), the single-writer lock of the B-tree becomes a bottleneck that MCP’s server-based abstractions can more easily mitigate via queueing.

### C. The Synthesis: Sharc *via* MCP
The winning architecture is likely **Sharc-backed MCP Servers**.
1.  **MCP as the Pipe**: Provides the standard discovery and transport protocol that all LLMs speak.
2.  **Sharc as the State**: Before the MCP server sends context, it queries the local Sharc database. This gives the "Pipe" a "Brain"—persistent, verifiable, and graph-structured memory.

> **Correction**: Previous versions of this document listed the Agent Registry and Authority Ceilings as missing. As of Feb 2026, `_sharc_agents` and `AuthorityCeiling` are fully implemented and verified.


### C. Final Verdict
Sharc is currently a **superior data-integrity engine**, but it is not yet a **protocol replacement**. To bridge this gap, Sharc must move beyond "efficient reading" and solve the hard problems of decentralized key management and multi-master synchronization.

> [!TIP]
> **Recommended Reading**: For technical solutions to these gaps, see the [Distributed Trust Architecture](file:///c:/Code/Sharc/docs/DistributedTrustArchitecture.md) roadmap.
