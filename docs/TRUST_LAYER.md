# Sharc Trust Layer: Cryptographic Integrity for AI Memory

The Sharc Trust Layer provides a decentralized, verifiable ledger for AI agents to record their actions, thoughts, and state changes with absolute cryptographic proof.

## Key Concepts

### 1. Agent Self-Attestation
Every AI agent in the Sharc ecosystem is registered with a unique identity and a public key (using HMAC-SHA256 in Wasm environments for maximum performance). Agents sign their own registration and every subsequent ledger entry.

### 2. Verifiable Ledger
The `_sharc_ledger` table maintains a hash-chain of all agent activities. Each entry includes:
- **PayloadHash**: SHA-256 hash of the action data.
- **PreviousHash**: Link to the preceding entry, forming a tamper-evident chain.
- **Signature**: HMAC-SHA256 signature from the acting agent.

### 3. Authority Ceiling
Agents can be assigned an "Authority Ceiling" which limits the maximum sequence number they can sign, or restricts their write scope to specific tables.

## Quickstart

```csharp
var ledger = new LedgerManager(db);

// Append a new entry
ledger.Append("agent-007", Encoding.UTF8.GetBytes("Action: Accessed context window."));

// Verify the entire chain
bool isValid = ledger.VerifyIntegrity();
Console.WriteLine($"Ledger Integrity: {isValid}");
```
