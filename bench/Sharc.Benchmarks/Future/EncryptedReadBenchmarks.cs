using BenchmarkDotNet.Attributes;

namespace Sharc.Benchmarks.Future;

/// <summary>
/// PLACEHOLDER: Encrypted database read benchmarks.
/// Requires: Sharc.Crypto implementation, AES-256-GCM page transform (Milestone 6).
/// Planned: Per-page decrypt at various page sizes, Argon2id KDF cost,
/// encrypted full table scan overhead.
/// </summary>
[BenchmarkCategory("Future", "Crypto")]
[MemoryDiagnoser]
public class EncryptedReadBenchmarks
{
    // Activate when Sharc.Crypto page transforms are implemented.
}
