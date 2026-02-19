# Trust Layer

Sharc includes a cryptographic agent trust layer for AI multi-agent coordination: ECDSA self-attestation, hash-chain audit ledger, entitlement enforcement, and co-signatures.

## Agent Registration

```csharp
using Sharc;
using Sharc.Trust;
using Sharc.Core.Trust;

using var db = SharcDatabase.Create("trusted.db");
var registry = new AgentRegistry(db);

// Create a signer (ECDSA key pair)
var signer = new SharcSigner("agent-007");

// Build agent identity
var agent = new AgentInfo(
    AgentId: "agent-007",
    Class: AgentClass.Local,
    PublicKey: signer.GetPublicKey(),
    AuthorityCeiling: 100,
    WriteScope: "users,logs",       // Comma-separated table list
    ReadScope: "*",                 // Wildcard = all tables
    ValidityStart: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    ValidityEnd: DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
    ParentAgent: "",
    CoSignRequired: false,
    Signature: Array.Empty<byte>()  // Self-signed during registration
);

registry.RegisterAgent(agent);
```

## Agent Classes

| Class | Value | Description |
|-------|-------|-------------|
| `Local` | 0 | Local agent on same machine |
| `Remote` | 1 | Remote agent over network |
| `Service` | 2 | Service/system agent |

## Entitlement Enforcement

Agent scopes are enforced on both reads and writes:

```csharp
// Read enforcement — agent can only read allowed tables/columns
using var reader = db.Query("SELECT * FROM users", agent);

// Write enforcement — agent can only write to allowed tables
using var writer = SharcWriter.Open("trusted.db");
writer.Insert(agent, "users", /* values */);  // OK if WriteScope includes "users"
writer.Insert(agent, "secrets", /* values */); // Throws UnauthorizedAccessException
```

### Scope Syntax

- `"*"` — wildcard, all tables allowed
- `"users,logs"` — comma-separated table list
- `"users.name,users.email"` — column-level granularity

## Immutable Ledger

Append-only hash-chain for audit trail:

```csharp
var ledger = new LedgerManager(db);

// Append a text payload
var payload = new TrustPayload(PayloadType.Text, "Mission complete");
ledger.Append(payload, signer);

// Append with context
ledger.Append("Agent processed 42 records", signer);
```

Each ledger entry includes:
- Agent ID and signature (ECDSA)
- Previous entry hash (chain integrity)
- Timestamp
- Payload

## Security Events

Both `AgentRegistry` and `LedgerManager` emit security events:

```csharp
registry.SecurityAudit += (sender, e) =>
{
    Console.WriteLine($"Security event: {e.EventType} — {e.Message}");
};
```

## ISharcSigner

Interface for custom signing implementations:

```csharp
public interface ISharcSigner
{
    string AgentId { get; }
    int SignatureSize { get; }
    bool TrySign(ReadOnlySpan<byte> dataToSign, byte[] signatureBuffer, out int signatureLength);
}
```
