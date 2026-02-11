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
    /// <summary>Argon2id — recommended default.</summary>
    Argon2id = 1,

    /// <summary>scrypt — alternative KDF.</summary>
    Scrypt = 2
}

/// <summary>
/// Supported cipher algorithms for page-level encryption.
/// </summary>
public enum SharcCipherAlgorithm : byte
{
    /// <summary>AES-256-GCM — hardware-accelerated on modern CPUs.</summary>
    Aes256Gcm = 1,

    /// <summary>XChaCha20-Poly1305 — constant-time on all hardware.</summary>
    XChaCha20Poly1305 = 2
}
