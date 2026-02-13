# Sharc Ledger: Feature Specification — PRC

This document details the features and technical capabilities of the Sharc Distributed Ledger, the foundational component of the **Agent Trust Layer**.

---

## 1. Executive Summary

In a multi-agent ecosystem, trust is established through verifiable provenance and non-repudiable attribution. The Sharc Ledger provides a high-performance, distributed, and append-only audit trail that allows agents to share context with guaranteed cryptographic integrity.

## 2. High-Level Features

### F1: Immutable Lineage (Tamper-Evidence)
- **Description**: Every modification to the context space is recorded in a hash-linked chain.
- **Capability**: Any attempt to alter historical context or reorder interactions results in a broken hash chain detectable during an integrity scan.
- **Technical Implementation**: SHA-256 linkage on every row entry.

### F2: Agent-Specific Attribution (Non-Repudiation)
- **Description**: Every entry is cryptographically signed by the responsible agent.
- **Capability**: Provides definitive proof of "Who" provided "What" and "When". Agents cannot deny their actions once committed to the ledger.
- **Technical Implementation**: ECDsa (P-256) signatures integrated at the record level.

### F3: Acid-Synchronized Audit Trails
- **Description**: Ledger updates are synchronized with database transactions.
- **Capability**: Guarantees that the audit trail exactly matches the current state of the database. If a data write fails, the corresponding ledger entry is never written (and vice versa).
- **Technical Implementation**: Integration with Sharc's Rollback Journal and Shadow Paging.

### F4: Lightweight Verification (Pilot-Ready)
- **Description**: Ultra-fast integrity verification suitable for browser and mobile environments.
- **Capability**: Allows client-side agents to verify the entire trust context in milliseconds before processing incoming messages.
- **Technical Implementation**: Optimized B-tree cursor walking with zero-allocation decoding.

---

## 3. Technical Specifications

### Ledger Data Structure
The ledger utilizes a **Linear Hash Chain** backed by a standard SQLite-compatible Table B-tree. This ensures compatibility with existing SQLite tools while providing Sharc-native performance.

| Feature | Specification |
|---------|---------------|
| **Primary Hash** | SHA-256 (32 bytes) |
| **Signature Algorithm** | ECDsa P-256 (nistP256) |
| **Signature Format** | RAW bytes (SubjectPublicKeyInfo for Public Keys) |
| **Sequence Strategy** | Monotonic 64-bit Integer |
| **Timestamping** | Microsecond-precision Unix Epoch (UTC) |

### Performance Metrics (Target)
- **Append Latency**: < 1ms (including hashing and signing).
- **Verification Throughput**: > 50,000 entries/sec on desktop hardware.
- **Verification Latency (Mobile)**: < 50ms for 1,000 entries in WASM.

---

## 4. Architectural Integration

The Ledger is deeply integrated into the Sharc core:

- **Record Layer**: Custom serial types in `RecordEncoder.cs` support high-performance BLOB handling for hashes and signatures.
- **IO Layer**: Bypasses traditional SQL layers to write directly to the `_sharc_ledger` B-tree.
- **Trust Layer**: Exposed via `LedgerManager` in the `Sharc.Trust` namespace.

---

## 5. Security Model

- **Collision Resistance**: Uses SHA-256 to ensure that distinct context payloads cannot produce the same hash.
- **Malleability Protection**: The signature covers the Sequence Number and Previous Hash, preventing attackers from replaying valid signatures in different positions in the chain.
- **Isolation**: The ledger table (`_sharc_ledger`) is logically isolated from user-level data while sharing the same physical security (AES-GCM encryption).

---

## 6. Vision: The Future of Agent Trust

The Sharc Ledger is the first step towards a decentralized **Context Space Engineering** ecosystem where agents can operate autonomously with high-frequency, verifiable coordination.

> [!NOTE]
> Future iterations (C2-C4) will build upon this foundation to add Row-Level Entitlement (RLE) and cross-node context synchronization.
