# Sharc.Crypto

**Production-grade encryption extensions for the Sharc database engine.**

This package provides secure, page-level encryption for SQLite databases using modern cryptographic primitives.

## Features

- **AES-256-GCM**: Industry-standard authenticated encryption for every database page.
- **Argon2id**: Memory-hard key derivation to protect against brute-force attacks.
- **Zero-Dependency**: No native crypto libraries required; uses pure .NET implementations.
- **Secure Lifecycle**: `SharcKeyHandle` ensures keys are handled safely in memory.

## Quick Start

```csharp
using Sharc;
using Sharc.Crypto;

// Open an encrypted database
var options = new SharcOpenOptions 
{ 
    Password = "your-secure-password" 
};

using var db = SharcDatabase.Open("secure_context.db", options);

// Reads are transparently decrypted at the page level 
// with zero intermediate byte[] allocations.
```

[Full Documentation](https://github.com/revred/Sharc)
