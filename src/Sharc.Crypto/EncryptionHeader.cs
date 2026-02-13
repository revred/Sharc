using System.Buffers.Binary;

namespace Sharc.Crypto;

/// <summary>
/// Parses the 128-byte Sharc encryption header that prefixes encrypted database files.
/// </summary>
/// <remarks>
/// Header layout:
/// <code>
/// Offset  Size  Field
///   0      6    Magic: SHARC\x00
///   6      2    Version (major.minor)
///   8      1    KDF algorithm (1=Argon2id/PBKDF2, 2=scrypt)
///   9      1    Cipher algorithm (1=AES-256-GCM, 2=XChaCha20-Poly1305)
///  10      2    Reserved (zero)
///  12      4    KDF time cost / iterations
///  16      4    KDF memory cost (KiB)
///  20      1    KDF parallelism
///  21      3    Reserved (zero)
///  24     32    KDF salt
///  56     32    Key verification hash (HMAC-SHA256)
///  88      4    Encrypted page size
///  92      4    Total encrypted pages
///  96     32    Reserved for future use
/// </code>
/// </remarks>
public readonly struct EncryptionHeader
{
    /// <summary>Header size in bytes.</summary>
    public const int HeaderSize = 128;

    /// <summary>Magic bytes: SHARC\x00</summary>
    private static ReadOnlySpan<byte> MagicBytes => "SHARC\0"u8;

    /// <summary>KDF algorithm identifier.</summary>
    public byte KdfAlgorithm { get; }

    /// <summary>Cipher algorithm identifier.</summary>
    public byte CipherAlgorithm { get; }

    /// <summary>KDF time cost / iterations parameter.</summary>
    public int TimeCost { get; }

    /// <summary>KDF memory cost in KiB.</summary>
    public int MemoryCostKiB { get; }

    /// <summary>KDF parallelism parameter.</summary>
    public byte Parallelism { get; }

    /// <summary>32-byte KDF salt.</summary>
    public ReadOnlyMemory<byte> Salt { get; }

    /// <summary>32-byte key verification hash (HMAC-SHA256).</summary>
    public ReadOnlyMemory<byte> VerificationHash { get; }

    /// <summary>Inner SQLite page size in bytes.</summary>
    public int PageSize { get; }

    /// <summary>Total number of encrypted pages.</summary>
    public int PageCount { get; }

    /// <summary>Header version major number.</summary>
    public byte VersionMajor { get; }

    /// <summary>Header version minor number.</summary>
    public byte VersionMinor { get; }

    private EncryptionHeader(byte kdfAlgorithm, byte cipherAlgorithm,
        int timeCost, int memoryCostKiB, byte parallelism,
        ReadOnlyMemory<byte> salt, ReadOnlyMemory<byte> verificationHash,
        int pageSize, int pageCount, byte versionMajor, byte versionMinor)
    {
        KdfAlgorithm = kdfAlgorithm;
        CipherAlgorithm = cipherAlgorithm;
        TimeCost = timeCost;
        MemoryCostKiB = memoryCostKiB;
        Parallelism = parallelism;
        Salt = salt;
        VerificationHash = verificationHash;
        PageSize = pageSize;
        PageCount = pageCount;
        VersionMajor = versionMajor;
        VersionMinor = versionMinor;
    }

    /// <summary>
    /// Checks whether the given data starts with the Sharc encryption magic bytes.
    /// </summary>
    public static bool HasMagic(ReadOnlySpan<byte> data)
    {
        return data.Length >= 6 && data[..6].SequenceEqual(MagicBytes);
    }

    /// <summary>
    /// Parses an encryption header from the first 128 bytes of a Sharc-encrypted file.
    /// </summary>
    /// <exception cref="InvalidOperationException">Invalid magic or insufficient data.</exception>
    public static EncryptionHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidOperationException($"Encryption header must be at least {HeaderSize} bytes.");

        if (!HasMagic(data))
            throw new InvalidOperationException("Invalid Sharc encryption magic.");

        byte versionMajor = data[6];
        byte versionMinor = data[7];
        byte kdfAlgorithm = data[8];
        byte cipherAlgorithm = data[9];
        int timeCost = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int memoryCostKiB = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        byte parallelism = data[20];
        var salt = data.Slice(24, 32).ToArray();
        var verificationHash = data.Slice(56, 32).ToArray();
        int pageSize = BinaryPrimitives.ReadInt32LittleEndian(data[88..]);
        int pageCount = BinaryPrimitives.ReadInt32LittleEndian(data[92..]);

        return new EncryptionHeader(
            kdfAlgorithm, cipherAlgorithm,
            timeCost, memoryCostKiB, parallelism,
            salt, verificationHash,
            pageSize, pageCount,
            versionMajor, versionMinor);
    }

    /// <summary>
    /// Writes a 128-byte encryption header to the given buffer.
    /// Used by test helpers to create encrypted test databases.
    /// </summary>
    public static void Write(Span<byte> destination, byte kdfAlgorithm, byte cipherAlgorithm,
        int timeCost, int memoryCostKiB, byte parallelism,
        ReadOnlySpan<byte> salt, ReadOnlySpan<byte> verificationHash,
        int pageSize, int pageCount)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination must be at least 128 bytes.");

        destination.Clear();
        MagicBytes.CopyTo(destination);
        destination[6] = 1; // version major
        destination[7] = 0; // version minor
        destination[8] = kdfAlgorithm;
        destination[9] = cipherAlgorithm;
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], timeCost);
        BinaryPrimitives.WriteInt32LittleEndian(destination[16..], memoryCostKiB);
        destination[20] = parallelism;
        salt[..32].CopyTo(destination[24..]);
        verificationHash[..32].CopyTo(destination[56..]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[88..], pageSize);
        BinaryPrimitives.WriteInt32LittleEndian(destination[92..], pageCount);
    }
}
