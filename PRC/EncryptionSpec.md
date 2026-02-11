# Encryption Specification — Sharc v0.1

## 1. Goals

- Encrypted-at-rest SQLite databases readable by Sharc
- Password-derived keys using modern KDFs
- Page-level encrypt/decrypt for streaming access
- No key material persisted on managed heap longer than necessary
- Forward-compatible versioned file header

## 2. Sharc Encryption File Format

### 2.1 Sharc Encrypted Header (128 bytes, prepended to database)

A Sharc-encrypted file is a standard SQLite database with each page encrypted,
prefixed by a 128-byte Sharc encryption header.

| Offset | Size | Field |
|--------|------|-------|
| 0      | 8    | Magic: `SHARC\x00\x01\x00` (6-byte magic + 2-byte version) |
| 8      | 1    | KDF algorithm (1 = Argon2id, 2 = scrypt) |
| 9      | 1    | Cipher algorithm (1 = AES-256-GCM, 2 = XChaCha20-Poly1305) |
| 10     | 2    | Reserved (zero) |
| 12     | 4    | KDF time cost / iterations |
| 16     | 4    | KDF memory cost (KiB) |
| 20     | 1    | KDF parallelism |
| 21     | 3    | Reserved (zero) |
| 24     | 32   | KDF salt |
| 56     | 32   | Key verification hash (HMAC-SHA256 of known plaintext) |
| 88     | 4    | Encrypted page size (matches inner SQLite page size) |
| 92     | 4    | Total encrypted pages |
| 96     | 32   | Reserved for future use (zero) |

**Total**: 128 bytes

### 2.2 Encrypted Page Layout

Each page in the file is stored as:

```
[12-byte nonce/IV][encrypted_page_data][16-byte auth_tag]
```

- **AES-256-GCM**: 12-byte nonce, 16-byte tag
- **XChaCha20-Poly1305**: 24-byte nonce, 16-byte tag

Nonce derivation: `nonce = HMAC-SHA256(key, page_number || counter)` truncated
to required nonce length. This is deterministic per page for read-only access.
Counter increments on re-encryption (write support, future).

**Associated data (AAD)**: page number as 4-byte big-endian — prevents page swapping.

### 2.3 File Structure

```
[128-byte Sharc header]
[encrypted page 1 (nonce + ciphertext + tag)]
[encrypted page 2 (nonce + ciphertext + tag)]
...
[encrypted page N (nonce + ciphertext + tag)]
```

## 3. Key Derivation

### 3.1 Default: Argon2id

Parameters (v0.1 defaults, configurable):
- Time cost: 3 iterations
- Memory cost: 65536 KiB (64 MiB)
- Parallelism: 4
- Output length: 32 bytes (256-bit key)
- Salt: 32 bytes, cryptographically random

### 3.2 Alternative: scrypt

- N: 2^17 (131072)
- r: 8
- p: 1
- Output length: 32 bytes

### 3.3 Key Verification

After derivation, compute `HMAC-SHA256(derived_key, "SHARC_KEY_VERIFY")`.
Compare against stored verification hash. Constant-time comparison required.
This verifies the password without exposing the key.

## 4. Key Lifecycle & Memory Safety

### 4.1 Requirements

- Derived key stored in pinned, zero-on-dispose buffer
- `SharcKeyHandle` implements `IDisposable`; zeroes key bytes on dispose
- Key never copied to managed `byte[]` that GC can relocate
- Use `GCHandle.Alloc(pinnedArray, GCHandleType.Pinned)` or
  `NativeMemory.AlignedAlloc` for key storage

### 4.2 SharcKeyHandle (Internal)

```csharp
internal sealed class SharcKeyHandle : IDisposable
{
    private readonly unsafe byte* _key;
    private readonly int _length;
    private bool _disposed;

    // Provides ReadOnlySpan<byte> access to key
    // Zeroes memory on Dispose
    // Destructor logs warning if not disposed
}
```

## 5. Cipher Implementations

### 5.1 AES-256-GCM (Default)

- Uses `System.Security.Cryptography.AesGcm` (.NET 8+)
- No external dependencies
- Hardware-accelerated on modern CPUs (AES-NI)

### 5.2 XChaCha20-Poly1305 (Alternative)

- Requires `libsodium` via managed binding or pure C# implementation
- Better constant-time properties on non-AES-NI hardware
- **Decision for v0.1**: AES-256-GCM as default; XChaCha20 deferred to v0.2

## 6. IPageTransform Interface

Encryption integrates via the page transform pipeline:

```csharp
public interface IPageTransform
{
    int TransformedPageSize(int rawPageSize);
    void TransformRead(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber);
    void TransformWrite(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber);
}
```

- `DecryptingPageTransform` : decrypts pages on read
- `IdentityPageTransform` : pass-through for unencrypted databases

## 7. Compatibility Notes

### What "SQLite-compatible encryption" means for Sharc:

- **Sharc encryption is NOT compatible with SQLCipher, SEE, or wxSQLite3.**
  These use different formats, different KDFs, and different page layouts.
- Sharc defines its own encryption envelope that wraps standard SQLite pages.
- An unencrypted Sharc database IS a standard SQLite database file.
- A Sharc-encrypted database can only be read by Sharc or tools implementing
  the Sharc encryption spec (this document).

### Migration Path

- Encrypt: Read plain SQLite DB → write Sharc-encrypted DB (future write support)
- Decrypt: Read Sharc-encrypted DB → write plain SQLite DB (future write support)
- For v0.1: Sharc can READ encrypted databases; encryption tooling deferred.

## 8. Test Vectors

See `tests/Sharc.Tests/Crypto/` for:
- Known KDF input/output pairs
- Known plaintext → ciphertext pairs per cipher
- Page encryption round-trip tests
- Key verification tests
- Tampered page detection tests
