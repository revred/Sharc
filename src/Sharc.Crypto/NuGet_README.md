# Sharc.Crypto

**Production-grade encryption extensions for the Sharc database engine.**

Page-level encryption for SQLite databases using modern cryptographic primitives, with zero native dependencies.

## Features

- **AES-256-GCM**: Authenticated encryption for every database page.
- **Argon2id**: Memory-hard key derivation to protect against brute-force attacks.
- **Zero-Dependency**: Pure .NET cryptographic implementations — no native libraries.
- **Secure Lifecycle**: `SharcKeyHandle` ensures keys are handled safely in memory.

## Quick Start

```csharp
using Sharc;

// Open an encrypted database
var options = new SharcOpenOptions { Password = "your-secure-password" };
using var db = SharcDatabase.Open("secure_data.db", options);

// Reads are transparently decrypted at the page level
// with zero intermediate byte[] allocations.
using var reader = db.CreateReader("users");
while (reader.Read())
    Console.WriteLine(reader.GetString(1));
```

[Full Documentation](https://github.com/revred/Sharc)
