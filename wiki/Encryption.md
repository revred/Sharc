# Encryption

Sharc provides transparent page-level encryption using AES-256-GCM with Argon2id key derivation. Requires the `Sharc.Crypto` package.

## Opening an Encrypted Database

```csharp
using Sharc;

var options = new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions
    {
        Password = "your-password"
    }
};

using var db = SharcDatabase.Open("secure.db", options);
// All reads are transparently decrypted — API is identical to unencrypted
using var reader = db.CreateReader("users");
```

## Encryption Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Password` | `string` | **required** | Passphrase for key derivation |
| `Kdf` | `SharcKdfAlgorithm` | `Argon2id` | Key derivation function |
| `Cipher` | `SharcCipherAlgorithm` | `Aes256Gcm` | Encryption algorithm |

## Algorithms

### Key Derivation (KDF)

| Algorithm | Description |
|-----------|-------------|
| `Argon2id` | Memory-hard, GPU-resistant. Recommended default |
| `Scrypt` | Alternative memory-hard KDF |

### Ciphers

| Algorithm | Description |
|-----------|-------------|
| `Aes256Gcm` | AES-256 in GCM mode. Hardware-accelerated (AES-NI) |
| `XChaCha20Poly1305` | Constant-time. Preferred when AES-NI is unavailable |

## Security Details

- Encryption operates at the **page level** — each 4096-byte page is independently encrypted
- Key material is held in **pinned memory** and zeroed on disposal
- Authentication tags (GCM/Poly1305) provide tamper detection
- Wrong passwords throw `SharcCryptoException`
- Reserved bytes in the SQLite header store the per-page nonce and auth tag
