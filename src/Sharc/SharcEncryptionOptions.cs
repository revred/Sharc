/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc;

/// <summary>
/// Encryption settings for Sharc databases.
/// </summary>
public sealed class SharcEncryptionOptions
{
    /// <summary>
    /// The password used to derive the encryption key.
    /// Cleared from memory after key derivation.
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// The KDF algorithm to use. Default is Argon2id.
    /// </summary>
    public SharcKdfAlgorithm Kdf { get; set; } = SharcKdfAlgorithm.Argon2id;

    /// <summary>
    /// The cipher algorithm to use. Default is AES-256-GCM.
    /// </summary>
    public SharcCipherAlgorithm Cipher { get; set; } = SharcCipherAlgorithm.Aes256Gcm;
}

/// <summary>
/// Supported key derivation functions.
/// </summary>
public enum SharcKdfAlgorithm : byte
{
    /// <summary>Argon2id â€” recommended default.</summary>
    Argon2id = 1,

    /// <summary>scrypt â€” alternative KDF.</summary>
    Scrypt = 2
}

/// <summary>
/// Supported cipher algorithms for page-level encryption.
/// </summary>
public enum SharcCipherAlgorithm : byte
{
    /// <summary>AES-256-GCM â€” hardware-accelerated on modern CPUs.</summary>
    Aes256Gcm = 1,

    /// <summary>XChaCha20-Poly1305 â€” constant-time on all hardware.</summary>
    XChaCha20Poly1305 = 2
}
