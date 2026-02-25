# The Arc Format (.arc)

## Overview

The `.arc` format is the native storage standard for the Sharc Database. Designed for edge computing, distributed systems, and multi-agent environments, an `.arc` file is highly adaptable. It represents the state of a Sharc database but is not constrained to a traditional monolithic file structure.

An `.arc` instance can exist as:
- A **Single Master File** persisting an entire system's state on disk.
- A **Single In-Memory File** providing high-speed, volatile storage for rapid event processing (commonly used in browser environments).
- A **Sharded Fragment** (an "arc" of the whole) where data is distributed across multiple files, devices, or network nodes.

### The Latitude and Longitude Analogy

Imagine a global dataset represented as a sphere. A single `.arc` file does not need to contain the entire sphere. Instead, it can represent a specific "arc segment" (e.g., a specific latitude or longitude). 

Different concerns are stored in different arc files. For example, in an avionics simulator:
- `engine_telemetry.arc` contains data authored by engine sensors.
- `navigation.arc` contains GPS and altimeter data.

The global situation—the "True State" of the system—is presented as a collection of these arc files. Each `.arc` file can exist autonomously. They can be located entirely on the server, entirely on the client side, or distributed across both. 

Because fragments are constructed as the situation becomes clearer, some duplication of information may be present across two separate arc formats. The total, comprehensive picture is obtained by computing the **union** of all records across all relevant `.arc` files.

---

## Multi-Agent, Zero-Trust Environments

In modern distributed systems, multiple AI agents or IoT sensors act autonomously. These agents operate in a zero-trust environment where they must author information into different channels or draw inferences from whatever full or partial datasets are available to them.

### Framework Protocol: Syncing and Filtering

To ensure agents can construct a coherent worldview from fragmented `.arc` files, the following protocol is applied:

1. **Cryptographic Signatures**: Every ledger entry within an `.arc` file is cryptographically signed by its authoring agent (e.g., using ECDSA P-256). When merging `.arc` files, signatures validate the unbroken chain of trust, regardless of where the file was hosted.
2. **Union by Sequence and Timestamp**: Because Sharc acts as an append-only ledger for state transitions, merging two `.arc` files involves unioning their ledgers. The consensus layer orders events by global timestamps and sequence numbers, resolving duplication naturally through idempotency.
3. **Contextual Filtering**: Agents rarely need the entire global state. Sharc supports fetching partial `.arc` files via strict query filters. If an agent is only responsible for Engine diagnostics, it can sync `GlobalState.arc` with a filter applied, downloading only the records authored by engine subsets, generating a localized partial dataset.

---

## Deployment Strategies

While Sharc is incredibly efficient, traditional disk-based databases can grow to enormous sizes that are unsuitable for constrained environments. The `.arc` format strategy adapts based on the deployment target:

### 1. Browser Environments (In-Memory & OPFS)
In most browser situations, managing a massive `.arc` file on disk is an anti-pattern. Instead:
- Sharc initializes as an **In-Memory** database.
- The browser caches fragments of the state locally. 
- Using the **Origin Private File System (OPFS)**, the browser can construct a persistent `.arc` file containing *only* information relevant to the local user's current browsing session or dataset.

### 2. Context Windows for LLMs
Large Language Models (LLMs) have finite context windows. 
- Rather than dumping raw text into a prompt, an `.arc` file can be serialized dynamically to act as a structured, queryable memory store.
- Agents can query the `.arc` file for relevant schema definitions or past interactions, drawing specific fragments (small arc segments) into the LLM's active working memory.

### 3. Distributed Cache (Redis Replacement)
When scaling out, `.arc` files can replace traditional key-value stores like Redis.
- Instead of a massive centralized cache, shards of `.arc` files are distributed across microservices.
- High-speed reads occur in local memory.
- Writes append to the local `.arc` and synchronize their trust payloads across the network map, achieving eventual consistency via the union protocol.

### 4. Sensor Sharding (Simulator Application)
In complex machinery (like a flight simulator), data cardinality is immense.
- High-frequency, low-trust data (e.g., raw heat sensor noise) is piped into a volatile `sensors_raw.arc`.
- Critical, threshold-crossing events (e.g., "Fire Detected") are extracted, signed by an authoritative agent, and placed into a durable `critical_events.arc`.
- The Master AutoPilot agent evaluates the union of both to make split-second decisions without being bogged down by the sheer volume of the raw data.

---

## Zero-Trust Integrity Model

The `.arc` format operates under a strict zero-trust assumption: no arc file, no agent, and no storage medium is inherently trusted. Trust is established cryptographically at every layer. This applies universally — whether the `.arc` files serve an avionics simulator, a supply chain tracker, a multi-agent AI pipeline, or a browser-based collaboration tool.

### Hash-Chain Integrity

Every ledger entry within an `.arc` file is chained to its predecessor via a cryptographic hash:

```text
Entry[n].Signature = Sign(PreviousHash(32) || PayloadHash(32) || Sequence(8))
```

This means:

- **No entry can be inserted, removed, or reordered** without breaking the chain.
- **No entry can be forged** without possessing the signing agent's private key.
- **Verification is streaming and zero-allocation** — the entire chain can be validated in a single forward pass without materializing all entries into memory.

### Agent Identity Verification

Before any payload is accepted into an arc's ledger:

1. The `AgentRegistry` confirms the signer is a registered agent.
2. The agent's `AuthorityCeiling` is checked against the payload's `EconomicValue` — an agent cannot sign payloads above its authority level.
3. The signature is verified against the agent's registered public key using the appropriate algorithm (HMAC-SHA256 or ECDSA P-256).

Agents must be registered in **every arc** that will receive their payloads. This prevents a rogue arc from accepting entries signed by unknown agents.

### Handling Lost Arc Fragments

Arc files are designed for graceful degradation when fragments are lost:

| Arc Type  | Durability     | Loss Impact                          | Recovery Strategy                                              |
| --------- | -------------- | ------------------------------------ | -------------------------------------------------------------- |
| Volatile  | In-memory only | Expected — ephemeral data by design  | Recreate empty; new data fills it immediately                  |
| Durable   | Persisted      | Significant — historical events lost | Restore from persistent store; if corrupt, discard and rebuild |
| Identity  | Persisted      | Moderate — agent history lost        | Re-register agents; continuity restarts from current session   |

The key principle: **loss of a fragment degrades history, not capability**. The system continues operating with whatever arcs are available. When a durable arc is restored from persistent storage (OPFS, disk, or network), its integrity is verified before use — if the hash chain is broken, the entire arc is discarded rather than trusted.

### Defending Against Malicious or Fabricated Data

A "cooked" arc file — one containing fabricated, tampered, or replayed data — is detected and rejected through multiple layers:

#### Layer 1: Cryptographic Chain Verification

On restore or sync, `VerifyIntegrity()` walks the full hash chain. Any of these conditions cause rejection:

- A signature that doesn't verify against the agent's public key.
- A `PreviousHash` that doesn't match the hash of the preceding entry.
- A sequence number gap or out-of-order entry.
- An entry signed by an unregistered agent.

#### Layer 2: Byzantine Consensus

Even if an agent produces correctly-signed but *semantically false* data (e.g., a sensor reporting impossible values, an AI agent fabricating citations, or a microservice injecting stale prices), the consensus layer detects this:

- **Quorum voting**: A supervising agent requires agreement from a majority of data sources before accepting a value.
- **Median outlier detection**: Values that deviate significantly from the median across all sources are flagged and rejected.
- **Dynamic reputation scoring**: Agents that consistently produce outlier or rejected data have their reputation degraded. Below a threshold, their contributions are automatically ignored.

#### Layer 3: Cross-Arc Isolation

Each arc maintains an **independent hash chain**. A compromised arc cannot affect any other arc because:

- Each arc has its own `LedgerManager` with its own chain state.
- There is no cross-arc chain dependency — the union is computed at the application layer, not the cryptographic layer.
- An attacker would need to compromise all arcs independently to subvert the system.

### Trust Confidence Hierarchy

```text
┌─────────────────────────────────────────────────────────────┐
│  Application Layer: Byzantine Consensus + Reputation        │
│  "Is this data consistent with what other agents report?"   │
├─────────────────────────────────────────────────────────────┤
│  Routing Layer: Economic Value Classification               │
│  "Does this payload warrant durable or volatile storage?"   │
├─────────────────────────────────────────────────────────────┤
│  Ledger Layer: Hash-Chain + Signature Verification          │
│  "Is this entry cryptographically valid and properly chained?" │
├─────────────────────────────────────────────────────────────┤
│  Agent Layer: Registry + Authority Ceiling                  │
│  "Is the signer registered and authorized for this value?"  │
├─────────────────────────────────────────────────────────────┤
│  Storage Layer: Persistence + Integrity Check               │
│  "Is the restored data intact and untampered?"              │
└─────────────────────────────────────────────────────────────┘
```

Every layer is independent. A failure at one layer does not cascade — it results in rejection at that layer, with the system continuing to operate using whatever trustworthy data remains.

### Real-World Applications

The zero-trust model applies identically across domains:

- **Multi-agent AI pipelines**: Each LLM agent writes to its own arc. A supervisor agent evaluates the union, rejecting hallucinated or contradictory outputs via quorum consensus.
- **IoT / edge computing**: Sensors write to volatile arcs locally. Critical threshold events promote to durable arcs synced upstream. A lost edge device loses only its volatile history.
- **Supply chain / logistics**: Each participant (warehouse, carrier, customs) maintains its own arc. The global state is the union of all arcs. A forged shipment record fails chain verification at the receiving party.
- **Collaborative editing**: Each user's edits go to their own arc. Merge conflicts are resolved by timestamp ordering and quorum, not by trusting any single participant's version.
- **Financial audit trails**: High-value transactions route to durable arcs with stricter authority ceilings and co-signature requirements. Low-value telemetry stays volatile.
- **Adversarial multi-agent gaming**: See below.

### Adversarial Multi-Agent Scenario

Consider a multiplayer game where each player is backed by a hierarchy of AI agents. Player A has a master strategist agent delegating to specialist sub-agents (scout, economist, diplomat). Player B is a rival master agent operating its own hierarchy. Both sides write to shared and private arcs.

```text
Player A                              Player B
┌─────────────────────┐               ┌─────────────────────┐
│  Master Strategist   │               │  Rival Master Agent  │
│  (high authority)    │               │  (high authority)    │
├──────┬──────┬────────┤               ├──────┬──────┬────────┤
│Scout │Econ  │Diplomat│               │Scout │Econ  │Diplomat│
│Agent │Agent │Agent   │               │Agent │Agent │Agent   │
└──┬───┴──┬───┴───┬────┘               └──┬───┴──┬───┴───┬────┘
   │      │       │                       │      │       │
   ▼      ▼       ▼                       ▼      ▼       ▼
 scout.arc econ.arc diplo.arc          scout.arc econ.arc diplo.arc
 (private) (private) (shared)          (private) (private) (shared)
```

The `.arc` format provides the trust infrastructure for this:

- **Private arcs are invisible**: Each player's internal strategy arcs (`scout.arc`, `econ.arc`) are local. The rival cannot read or inject into them because the agent registry only contains that player's agents.
- **Shared arcs enforce honesty**: The `diplo.arc` (diplomacy channel) is shared. Both players' diplomat agents are registered. Every proposal, treaty, or trade is cryptographically signed. A player cannot later deny a commitment — the hash chain is immutable.
- **Authority ceilings prevent escalation**: Sub-agents have limited `AuthorityCeiling`. A scout agent (ceiling: 10) cannot sign a high-value trade agreement (value: 500) — only the master strategist can. If a compromised sub-agent attempts it, the ledger rejects the payload.
- **Reputation tracks reliability**: If Player B's diplomat agent repeatedly breaks treaties (detected by the application layer comparing stated intentions against observed actions), its reputation degrades. Player A's master agent can configure a policy: ignore proposals from agents with reputation below 30.
- **Cross-arc isolation prevents sabotage**: Even if Player B somehow obtains a copy of Player A's `scout.arc`, they cannot inject false intelligence into it — they lack the private keys of Player A's scout agent. And even a perfectly forged arc would fail `VerifyIntegrity()` because the chain hashes won't match.
- **Fragment loss is tactical, not fatal**: If Player A's `econ.arc` is lost (network partition, device failure), the master strategist loses economic history but continues operating with whatever data the scout and diplomat arcs provide. The system degrades gracefully — it never halts.

This pattern generalizes to any adversarial multi-party system: competitive marketplaces, distributed auctions, federated learning across rival organizations, or multi-tenant SaaS platforms where tenants must not be able to tamper with each other's audit trails.
