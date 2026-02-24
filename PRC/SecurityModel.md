# Security Model — Sharc

## 1. Threat Model

### 1.1 Assets Under Protection

| Asset | Description |
|-------|-------------|
| Database content | User data stored in SQLite tables |
| Encryption key | Derived from user password; protects confidentiality |
| Key material in memory | Transient key bytes during operation |
| Agent entitlements | Scoped read/write access grants for multi-agent trust layer |

### 1.2 Threat Actors

| Actor | Capability | Goal |
|-------|-----------|------|
| File-system attacker | Read/copy database files at rest | Access encrypted data |
| Memory forensics attacker | Read process memory (crash dump, swap file) | Extract encryption key |
| Malicious database author | Craft a SQLite file designed to exploit the parser | Crash, DoS, or code execution |
| Rogue agent (trust layer) | Craft SQL to exceed granted entitlements | Read/write columns or tables outside scope |
| Network attacker | Intercept database in transit | Access data (out of scope — Sharc is not a transport layer) |

### 1.3 Trust Boundaries

```
┌─────────────────────────────────────────────┐
│ TRUSTED: Sharc Library Code                  │
│   - Parser logic                             │
│   - Cryptographic operations                 │
│   - Key derivation and management            │
│   - Entitlement enforcement                  │
├─────────────────────────────────────────────┤
│ SEMI-TRUSTED: Agent SQL Input                │
│   - Agents may be scoped but adversarial     │
│   - SQL text must pass entitlement check     │
│     BEFORE any execution path                │
├─────────────────────────────────────────────┤
│ UNTRUSTED: Input Data                        │
│   - Database files (may be malformed)        │
│   - In-memory buffers (may be corrupted)     │
│   - User-provided passwords (may be weak)    │
└─────────────────────────────────────────────┘
```

## 2. Known Vulnerabilities

> **Last reviewed**: 2026-02-24 (Codex snapshot review + Part 1/Part 2 critical reviews)

### SHARC-SEC-001 — Entitlement bypass on hinted query paths (HIGH)

**Status**: OPEN — known, not yet fixed

**Description**: The `CACHED` and `JIT` execution hint prefixes route queries through `ExecutionRouter.TryRoute()` at `SharcDatabase.cs:711-713`, which returns a reader **before** the entitlement enforcement at `SharcDatabase.cs:747-752`. A scoped agent that writes raw SQL with a `CACHED` or `JIT` prefix can bypass `EntitlementEnforcer.EnforceAll()` entirely.

**Affected code**:

- `src/Sharc/SharcDatabase.cs:707-713` — hint routing returns early
- `src/Sharc/SharcDatabase.cs:747-752` — entitlement check runs only on DIRECT path
- `src/Sharc.Query/Sharq/SharqParser.cs:101-117` — hint token parsing
- `src/Sharc/Query/ExecutionRouter.cs:61-68` — router dispatch

**Exploitability**: LOW in practice. Requires the attacker to:

1. Have an `AgentInfo` with restricted scope (i.e., be a registered agent)
2. Write raw SHARQ SQL with hint prefix syntax (`CACHED SELECT ...`)
3. Bypass the high-level API and MCP tool interfaces, which do not expose hint syntax

The high-level API (`SharcDatabase.Query()`), MCP context tools, and graph traversal APIs do not expose hint syntax to callers. Exploitation requires direct `QueryCore()` invocation with crafted SQL.

**Remediation plan** (WP-01):

1. Move entitlement enforcement **before** the `TryRoute()` call, or duplicate enforcement inside `ExecutionRouter.TryRoute()` when an agent is present.
2. Add negative tests: agent + `CACHED` hint → `UnauthorizedAccessException`.
3. Add negative tests: agent + `JIT` hint → `UnauthorizedAccessException`.
4. Audit `CompoundQueryExecutor` and `JoinExecutor` for equivalent bypass paths.

### SHARC-SEC-002 — Column-level entitlement incomplete for wildcard/aggregate (HIGH)

**Status**: OPEN — known, not yet fixed

**Description**: `SELECT *` does not expand to concrete column names before the entitlement check. `TableReferenceCollector` at line 104-106 registers the table with an empty column set when `intent.Columns` is null (wildcard). `EntitlementEnforcer.EnforceScope()` then calls `CanReadTable()` which succeeds if _any_ scope entry matches — column-level restrictions are never evaluated.

Additionally, aggregate expressions (`AVG(salary)`) and `CASE` source columns are not extracted by the collector. A column-scoped agent can read restricted columns through `SELECT AVG(restricted_col) FROM table` or `SELECT CASE WHEN restricted_col > 0 THEN 1 ELSE 0 END FROM table`.

**Affected code**:

- `src/Sharc/Query/TableReferenceCollector.cs:97-107` — wildcard registers empty column set
- `src/Sharc/Trust/ScopeDescriptor.cs:86-98` — `CanReadTable()` succeeds without column check
- `src/Sharc/Trust/EntitlementEnforcer.cs:98` — `columns == null` early return skips column enforcement

**Remediation plan** (WP-02):

1. Resolve `SELECT *` to schema-derived column list before enforcement.
2. Extract source columns from aggregate function arguments and `CASE` expressions.
3. Remove the `columns == null` early return — treat null columns as "all columns" and validate each.
4. Add tests: column-scoped agent + `SELECT *` → denied.
5. Add tests: column-scoped agent + `SELECT AVG(restricted)` → denied.

### SHARC-SEC-003 — JOIN residual filter mis-resolves unqualified columns (HIGH)

**Status**: OPEN — known, not yet fixed

**Description**: The JOIN executor builds a materialized schema with alias-qualified keys (`alias.column`) at `JoinExecutor.cs:412-417`. Residual `WHERE` predicates use `node.ColumnName` for lookup at `JoinExecutor.cs:690-693`. When the predicate references an unqualified column name (e.g., `WHERE id > 5` instead of `WHERE t.id > 5`), the lookup fails silently and defaults to `QueryValue.Null`. This causes incorrect filtering — rows that should be excluded are included, and vice versa.

**Affected code**:
- `src/Sharc/Query/Execution/JoinExecutor.cs:412-417` — schema keys are alias-qualified
- `src/Sharc/Query/Execution/JoinExecutor.cs:690-693` — direct `node.ColumnName` lookup
- `src/Sharc/Query/Execution/JoinExecutor.cs:627-630` — unqualified handling marked as "risky"

**Impact**: Correctness bug, not a data-leak vulnerability. However, in combination with SHARC-SEC-002, incorrect filtering could expose more rows than intended to a scoped agent.

**Remediation plan** (WP-03):
1. Build a fallback resolver that maps unqualified names to alias-qualified keys when unambiguous.
2. Reject ambiguous unqualified column references with a clear error.
3. Add tests for LEFT/RIGHT/FULL join `WHERE` with unqualified and qualified column names.

### SHARC-SEC-004 — Unaudited entitlement paths in newer assemblies (MEDIUM)

**Status**: OPEN — audit not yet performed

**Description**: The Codex review concentrated on `src/Sharc/` and `src/Sharc.Query/`. The following assemblies have not been reviewed for entitlement bypass patterns equivalent to SHARC-SEC-001:

- `src/Sharc.Arc/` — Arc import/export, `CsvArcImporter`, `FusedArcContext`
- `src/Sharc.Graph/` — Graph traversal, Cypher executor
- `tools/Sharc.Context/` — MCP context server
- `tools/Sharc.Index/` — GCD CLI

If any of these assemblies route execution before entitlement checks (or bypass them entirely), the same class of vulnerability exists.

**Remediation**: Security audit of all query/execution entry points in newer assemblies.

## 3. Entitlement Model

### 3.1 Design

Agents are issued scoped entitlements via `AgentInfo.ReadScope` and `AgentInfo.WriteScope`. Scopes are parsed into `ScopeDescriptor` entries that specify permitted tables and optionally permitted columns within those tables.

Enforcement is performed by `EntitlementEnforcer` which:
1. Validates agent identity (optional pluggable `IdentityValidator` hook)
2. Validates agent temporal window (`ValidityStart`/`ValidityEnd`)
3. Parses scope into `ScopeDescriptor`
4. Checks table-level access via `CanReadTable()`
5. Checks column-level access via `CanReadColumn()` when columns are provided

### 3.2 Enforcement Points

| Entry Point | Enforcement | Status |
|------------|-------------|--------|
| `SharcDatabase.QueryCore()` DIRECT path | `EntitlementEnforcer.EnforceAll()` at line 747 | Functional |
| `SharcDatabase.QueryCore()` CACHED/JIT path | **Missing** — returns before line 747 | SHARC-SEC-001 |
| `SharcDatabase.CreateReader()` | `EntitlementEnforcer.Enforce()` | Functional |
| `SharcDatabase.Insert/Update/Delete` | `EntitlementEnforcer.EnforceWrite()` | Functional |
| `SharcDatabase.CreateTable/DropTable` | `EntitlementEnforcer.EnforceSchemaAdmin()` | Functional |
| Cypher executor | Not reviewed | SHARC-SEC-004 |
| Graph traversal | Not reviewed | SHARC-SEC-004 |
| MCP context tools | Not reviewed | SHARC-SEC-004 |
| Arc import/export | Not reviewed | SHARC-SEC-004 |

### 3.3 Invariant

> **No query execution path shall return data before entitlement validation completes.**

This invariant is currently violated by SHARC-SEC-001. All remediation work must restore and maintain this invariant.

## 4. Encryption Security

### 4.1 Key Derivation

**Algorithm**: Argon2id (RFC 9106)
**Parameters**: 3 iterations, 64 MiB memory, parallelism 4
**Salt**: 32 bytes, cryptographically random (`RandomNumberGenerator.Fill()`)
**Output**: 256-bit key

**Security properties**:
- Memory-hard: resists GPU/ASIC brute-force attacks
- Side-channel resistant: Argon2id hybrid mode
- Salt prevents rainbow table attacks
- 256-bit output matches cipher key size exactly

**Password strength**: Sharc does not enforce password requirements. This is the consumer's responsibility. Sharc's KDF parameters are designed to make brute-force expensive even with weak passwords (~300 ms per attempt on modern hardware).

### 4.2 Page-Level Encryption

**Algorithm**: AES-256-GCM
**Nonce**: 12 bytes, deterministically derived: `HMAC-SHA256(key, page_number || counter)` truncated to 12 bytes
**AAD (Associated Authenticated Data)**: 4-byte big-endian page number

**Security properties**:
- **Confidentiality**: AES-256 with unique nonce per page
- **Integrity**: GCM authentication tag (16 bytes) detects tampering
- **Page-swap protection**: AAD includes page number — moving page N's ciphertext to page M's slot causes authentication failure
- **Nonce uniqueness**: Deterministic nonce derived from key + page number guarantees uniqueness as long as the key doesn't change. For read-only access, this is always safe.

### 4.3 Key Verification

**Purpose**: Quickly validate password correctness without attempting full page decryption.

**Method**: `HMAC-SHA256(derived_key, "SHARC_KEY_VERIFY")` stored in the encryption header.

**Security properties**:
- Does not leak key bits (HMAC is one-way)
- Constant-time comparison prevents timing attacks
- Known plaintext ("SHARC_KEY_VERIFY") is public — this is not a weakness because HMAC security does not depend on message secrecy

### 4.4 Nonce Derivation Rationale

Why deterministic nonces instead of random?

- **Read-only safety**: Same page always produces the same nonce — no risk of nonce reuse in read path
- **No state needed**: No counter file, no nonce storage, no coordination
- **Reproducibility**: Same key + same page number always decrypts successfully

For future **write support**, the counter field in the derivation input must be incremented on re-encryption to avoid nonce reuse with the same key.

## 5. Memory Safety

### 5.1 Key Lifecycle

```
Password received
    |
    v
KDF derives 256-bit key into pinned buffer (SharcKeyHandle)
    |
    +-- Password string is NOT cleared (managed string, GC controls it)
    |   MITIGATION: Document that callers should minimize password lifetime
    |
    v
Key used for page decryption (passed as ReadOnlySpan<byte>)
    |
    v
SharcDatabase.Dispose() -> SharcKeyHandle.Dispose()
    |
    v
Key memory is zeroed (CryptographicOperations.ZeroMemory)
    |
    v
Pinned buffer is freed
```

### 5.2 Mitigations for In-Memory Key Exposure

| Threat | Mitigation |
|--------|-----------|
| GC relocates key buffer | `GCHandle.Alloc(Pinned)` prevents relocation |
| Key remains after use | `CryptographicOperations.ZeroMemory()` on dispose |
| Key in swap file | Not preventable in managed code; OS-level encryption recommended |
| Key in crash dump | Zeroed on dispose; transient exposure window is minimized |
| Multiple copies via span | Spans are stack-only references, not heap copies |

### 5.3 Limitations (Honest Assessment)

- **Managed strings are not securable**: The password `string` is on the managed heap and cannot be reliably zeroed. .NET's `SecureString` is deprecated. Mitigation: derive key immediately, advise callers to minimize password object lifetime.
- **JIT may copy stack values**: The JIT compiler might spill registers to stack. We cannot prevent this.
- **Defense in depth**: Memory protections are best-effort. Sharc's primary security guarantee is **encryption at rest**, not protection against a compromised process.

## 6. Parser Hardening

### 6.1 Malicious Input Defenses

Sharc treats all input database files as **untrusted**. Defenses:

| Attack Vector | Defense |
|--------------|---------|
| Oversized varint | Maximum 9 bytes enforced; longer sequences are rejected |
| Circular overflow chain | Track visited page numbers; detect cycles |
| Page number out of bounds | Validate against `PageCount` before any read |
| Enormous payload size | Cap at `int.MaxValue`; allocate via ArrayPool with limits |
| Cell pointer outside page | Validate offset < `PageSize` before access |
| Zero-length page | Reject pages smaller than minimum header size |
| Recursive b-tree depth | Cap traversal depth at `ceil(log2(PageCount)) + 10` |
| Enormous cell count | Validate `CellCount * 2 + HeaderSize <= PageSize` |

### 6.2 Integer Overflow Prevention

All size calculations use `checked` arithmetic or explicit overflow guards:

```csharp
// Safe: checked multiplication
int pageOffset = checked((int)(pageNumber - 1) * pageSize);

// Safe: explicit overflow check
long totalSize = (long)pageSize * pageCount;
if (totalSize > int.MaxValue)
    ThrowHelper.ThrowInvalidDatabase("Database too large for 32-bit addressing");
```

### 6.3 Span Bounds Safety

All span operations use slicing, which throws `ArgumentOutOfRangeException` on out-of-bounds access:

```csharp
// data.Slice(offset, length) throws if offset+length > data.Length
ReadOnlySpan<byte> cellData = pageData.Slice(cellOffset, cellLength);
```

This is a **safety net**, not the primary defense. Primary validation happens before span operations.

## 7. Cache Security

### 7.1 Known Issues

**Parameter cache key instability** (MEDIUM): `ExecutionRouter.ComputeParamKey()` at line 260-272 uses `HashCode` over dictionary iteration order. Dictionary iteration order is not guaranteed stable across .NET versions or between insertions. Two semantically identical parameter maps can produce different hash keys, causing cache misses. Conversely, 32-bit hash collisions can cause false cache hits, returning results for wrong parameters.

**Unbounded cache growth** (MEDIUM): The following caches grow without bound:
- `QueryPlanCache._cache` (`ConcurrentDictionary`, line 14)
- `ExecutionRouter._cachedQueries`, `_jitEntries`, `_directCachedCache`, `_directJitCache` (lines 25-33)
- `SharcDatabase._readerInfoCache`, `_paramFilterCache` (lines 75-76)

Under ad-hoc query workloads (e.g., parameterized queries with user-generated SQL), these caches can grow to consume significant memory.

### 7.2 Remediation Plan (WP-04)

1. Replace iteration-order-sensitive hash with canonical key derivation (sorted key enumeration).
2. Introduce max-size eviction policy (LRU or size-bounded) for all query caches.
3. Add cache size counters to diagnostics output.
4. Add regression tests for parameter ordering and hash collision scenarios.

## 8. Supply Chain Security

- **Zero runtime dependencies** (Sharc + Sharc.Core)
- **Sharc.Crypto**: Only `System.Security.Cryptography` (BCL) and potentially `Konscious.Security.Cryptography` for Argon2
- All dependencies are version-pinned
- No native code, no P/Invoke, no dynamic loading
- See `PRC/DependencyPolicy.md`

## 9. What Sharc Does NOT Protect Against

| Scenario | Why Not |
|----------|---------|
| Weak passwords | User responsibility; Sharc's KDF makes brute-force expensive but can't prevent it |
| Root/admin access to host | If the attacker owns the machine, they own the process memory |
| Side-channel attacks on AES | Hardware AES-NI provides constant-time operation; software fallback does not |
| Database modification | Write engine provides ACID transactions via rollback journal; B-tree mutations are crash-safe via ShadowPageSource copy-on-write. Encrypted writes re-encrypt modified pages with fresh nonces. |
| Traffic interception | Sharc is not a transport protocol; use TLS for network transfer |
| Denial of service via large DB | Sharc will process whatever file it's given; callers should validate file size |

## 10. Security Testing

| Test Category | What's Tested |
|--------------|---------------|
| Known-answer KDF tests | Argon2id output matches reference vectors |
| Encryption round-trip | Encrypt -> decrypt = original plaintext |
| Tamper detection | Modified ciphertext -> authentication failure |
| Page swap detection | Page N ciphertext placed at page M -> authentication failure |
| Wrong password | Key verification hash mismatch -> clear error |
| Malformed encryption header | Missing/truncated header -> clear error |
| Truncated ciphertext | Short page -> authentication failure |
| Fuzz testing (future) | Random bytes as database -> no crash, clean errors |

### 10.1 Required Test Additions (from Codex + Critical Reviews)

| Test | Covers | Status |
|------|--------|--------|
| Agent + `CACHED` hint on restricted table -> denied | SHARC-SEC-001 | Not yet written |
| Agent + `JIT` hint on restricted table -> denied | SHARC-SEC-001 | Not yet written |
| Agent + `CACHED` hint on restricted column -> denied | SHARC-SEC-001 + 002 | Not yet written |
| Column-scoped agent + `SELECT *` -> denied | SHARC-SEC-002 | Not yet written |
| Column-scoped agent + `SELECT AVG(restricted)` -> denied | SHARC-SEC-002 | Not yet written |
| Column-scoped agent + `CASE` on restricted column -> denied | SHARC-SEC-002 | Not yet written |
| JOIN + unqualified column in WHERE -> correct filtering | SHARC-SEC-003 | Not yet written |
| JOIN + ambiguous unqualified column -> error | SHARC-SEC-003 | Not yet written |
| Parameter map {a=1,b=2} and {b=2,a=1} -> same cache key | WP-04 | Not yet written |
| Cache size does not exceed configured bound after N queries | WP-04 | Not yet written |

## 11. Remediation Priority

| ID | Severity | Work Package | Blocks |
|----|----------|-------------|--------|
| SHARC-SEC-001 | HIGH | WP-01: Entitlement Security Closure | Trust layer credibility |
| SHARC-SEC-002 | HIGH | WP-02: Column Scope Hardening | Trust layer credibility |
| SHARC-SEC-003 | HIGH | WP-03: JOIN Residual Correctness | Query correctness |
| SHARC-SEC-004 | MEDIUM | Audit pass on newer assemblies | Full coverage |
| Cache key instability | MEDIUM | WP-04: Cache Correctness | Production readiness |
| Unbounded caches | MEDIUM | WP-04: Cache Governance | Production readiness |

**Execution sequence**: WP-01 and WP-02 are existential for the trust story and must land first. WP-03 and WP-04 are correctness/stability and should run alongside early platform work. The assembly audit (SHARC-SEC-004) should start immediately as a parallel investigation — it may surface additional instances of the same vulnerability class.

## 12. Review History

| Date | Reviewer | Scope | Key Findings |
|------|----------|-------|-------------|
| 2026-02-24 | Codex | Snapshot: `src/Sharc/`, `src/Sharc.Query/` | SHARC-SEC-001, 002, 003; cache issues; doc contradictions |
| 2026-02-24 | Claude (Part 1 + Part 2) | Full architecture + platform vision | Trust layer gaps, platform sequencing, newer assembly blind spots |
| Pre-2026 | Internal | Encryption, parser, memory | Original sections 2-7 of this document |
