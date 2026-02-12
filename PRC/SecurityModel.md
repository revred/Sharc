# Security Model — Sharc

## 1. Threat Model

### 1.1 Assets Under Protection

| Asset | Description |
|-------|-------------|
| Database content | User data stored in SQLite tables |
| Encryption key | Derived from user password; protects confidentiality |
| Key material in memory | Transient key bytes during operation |

### 1.2 Threat Actors

| Actor | Capability | Goal |
|-------|-----------|------|
| File-system attacker | Read/copy database files at rest | Access encrypted data |
| Memory forensics attacker | Read process memory (crash dump, swap file) | Extract encryption key |
| Malicious database author | Craft a SQLite file designed to exploit the parser | Crash, DoS, or code execution |
| Network attacker | Intercept database in transit | Access data (out of scope — Sharc is not a transport layer) |

### 1.3 Trust Boundaries

```
┌─────────────────────────────────────────────┐
│ TRUSTED: Sharc Library Code                  │
│   - Parser logic                             │
│   - Cryptographic operations                 │
│   - Key derivation and management            │
├─────────────────────────────────────────────┤
│ UNTRUSTED: Input Data                        │
│   - Database files (may be malformed)        │
│   - In-memory buffers (may be corrupted)     │
│   - User-provided passwords (may be weak)    │
└─────────────────────────────────────────────┘
```

## 2. Encryption Security

### 2.1 Key Derivation

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

### 2.2 Page-Level Encryption

**Algorithm**: AES-256-GCM
**Nonce**: 12 bytes, deterministically derived: `HMAC-SHA256(key, page_number || counter)` truncated to 12 bytes
**AAD (Associated Authenticated Data)**: 4-byte big-endian page number

**Security properties**:
- **Confidentiality**: AES-256 with unique nonce per page
- **Integrity**: GCM authentication tag (16 bytes) detects tampering
- **Page-swap protection**: AAD includes page number — moving page N's ciphertext to page M's slot causes authentication failure
- **Nonce uniqueness**: Deterministic nonce derived from key + page number guarantees uniqueness as long as the key doesn't change. For read-only access, this is always safe.

### 2.3 Key Verification

**Purpose**: Quickly validate password correctness without attempting full page decryption.

**Method**: `HMAC-SHA256(derived_key, "SHARC_KEY_VERIFY")` stored in the encryption header.

**Security properties**:
- Does not leak key bits (HMAC is one-way)
- Constant-time comparison prevents timing attacks
- Known plaintext ("SHARC_KEY_VERIFY") is public — this is not a weakness because HMAC security does not depend on message secrecy

### 2.4 Nonce Derivation Rationale

Why deterministic nonces instead of random?

- **Read-only safety**: Same page always produces the same nonce — no risk of nonce reuse in read path
- **No state needed**: No counter file, no nonce storage, no coordination
- **Reproducibility**: Same key + same page number always decrypts successfully

For future **write support**, the counter field in the derivation input must be incremented on re-encryption to avoid nonce reuse with the same key.

## 3. Memory Safety

### 3.1 Key Lifecycle

```
Password received
    │
    ▼
KDF derives 256-bit key into pinned buffer (SharcKeyHandle)
    │
    ├── Password string is NOT cleared (managed string, GC controls it)
    │   MITIGATION: Document that callers should minimize password lifetime
    │
    ▼
Key used for page decryption (passed as ReadOnlySpan<byte>)
    │
    ▼
SharcDatabase.Dispose() → SharcKeyHandle.Dispose()
    │
    ▼
Key memory is zeroed (CryptographicOperations.ZeroMemory)
    │
    ▼
Pinned buffer is freed
```

### 3.2 Mitigations for In-Memory Key Exposure

| Threat | Mitigation |
|--------|-----------|
| GC relocates key buffer | `GCHandle.Alloc(Pinned)` prevents relocation |
| Key remains after use | `CryptographicOperations.ZeroMemory()` on dispose |
| Key in swap file | Not preventable in managed code; OS-level encryption recommended |
| Key in crash dump | Zeroed on dispose; transient exposure window is minimized |
| Multiple copies via span | Spans are stack-only references, not heap copies |

### 3.3 Limitations (Honest Assessment)

- **Managed strings are not securable**: The password `string` is on the managed heap and cannot be reliably zeroed. .NET's `SecureString` is deprecated. Mitigation: derive key immediately, advise callers to minimize password object lifetime.
- **JIT may copy stack values**: The JIT compiler might spill registers to stack. We cannot prevent this.
- **Defense in depth**: Memory protections are best-effort. Sharc's primary security guarantee is **encryption at rest**, not protection against a compromised process.

## 4. Parser Hardening

### 4.1 Malicious Input Defenses

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

### 4.2 Integer Overflow Prevention

All size calculations use `checked` arithmetic or explicit overflow guards:

```csharp
// ✓ Safe: checked multiplication
int pageOffset = checked((int)(pageNumber - 1) * pageSize);

// ✓ Safe: explicit overflow check
long totalSize = (long)pageSize * pageCount;
if (totalSize > int.MaxValue)
    ThrowHelper.ThrowInvalidDatabase("Database too large for 32-bit addressing");
```

### 4.3 Span Bounds Safety

All span operations use slicing, which throws `ArgumentOutOfRangeException` on out-of-bounds access:

```csharp
// data.Slice(offset, length) throws if offset+length > data.Length
ReadOnlySpan<byte> cellData = pageData.Slice(cellOffset, cellLength);
```

This is a **safety net**, not the primary defense. Primary validation happens before span operations.

## 5. Supply Chain Security

- **Zero runtime dependencies** (Sharc + Sharc.Core)
- **Sharc.Crypto**: Only `System.Security.Cryptography` (BCL) and potentially `Konscious.Security.Cryptography` for Argon2
- All dependencies are version-pinned
- No native code, no P/Invoke, no dynamic loading
- See `PRC/DependencyPolicy.md`

## 6. What Sharc Does NOT Protect Against

| Scenario | Why Not |
|----------|---------|
| Weak passwords | User responsibility; Sharc's KDF makes brute-force expensive but can't prevent it |
| Root/admin access to host | If the attacker owns the machine, they own the process memory |
| Side-channel attacks on AES | Hardware AES-NI provides constant-time operation; software fallback does not |
| Database modification | Sharc is read-only; write integrity is out of scope |
| Traffic interception | Sharc is not a transport protocol; use TLS for network transfer |
| Denial of service via large DB | Sharc will process whatever file it's given; callers should validate file size |

## 7. Security Testing

| Test Category | What's Tested |
|--------------|---------------|
| Known-answer KDF tests | Argon2id output matches reference vectors |
| Encryption round-trip | Encrypt → decrypt = original plaintext |
| Tamper detection | Modified ciphertext → authentication failure |
| Page swap detection | Page N ciphertext placed at page M → authentication failure |
| Wrong password | Key verification hash mismatch → clear error |
| Malformed encryption header | Missing/truncated header → clear error |
| Truncated ciphertext | Short page → authentication failure |
| Fuzz testing (future) | Random bytes as database → no crash, clean errors |
