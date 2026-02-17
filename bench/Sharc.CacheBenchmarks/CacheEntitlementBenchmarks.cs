// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Measures entitlement encryption overhead: encrypted vs unencrypted Get/Set,
/// scope key derivation caching, and multi-scope scenarios.
/// Spec target: encrypted Get &lt; 5 μs, encrypted Set &lt; 10 μs.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "Entitlement")]
public class CacheEntitlementBenchmarks
{
    private CacheEngine _plainEngine = null!;
    private CacheEngine _encryptedEngine = null!;
    private FakeEntitlementProvider _provider = null!;
    private byte[] _payload = null!;
    private string[] _keys = null!;
    private CacheEntryOptions _scopedOptions = null!;

    private const int KeyCount = 1000;

    [GlobalSetup]
    public void Setup()
    {
        _provider = new FakeEntitlementProvider { CurrentScope = "tenant:acme" };
        _payload = new byte[1024]; // 1 KB — typical cache value
        RandomNumberGenerator.Fill(_payload);

        var masterKey = new byte[32];
        RandomNumberGenerator.Fill(masterKey);

        _plainEngine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxCacheSize = 512L * 1024 * 1024,
        });

        _encryptedEngine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxCacheSize = 512L * 1024 * 1024,
            EnableEntitlement = true,
            MasterKey = masterKey,
        }, _provider);

        _keys = new string[KeyCount];
        _scopedOptions = new CacheEntryOptions { Scope = "tenant:acme" };

        for (int i = 0; i < KeyCount; i++)
        {
            _keys[i] = $"key:{i:D5}";
            _plainEngine.Set(_keys[i], _payload);
            _encryptedEngine.Set(_keys[i], _payload, _scopedOptions);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _plainEngine.Dispose();
        _encryptedEngine.Dispose();
    }

    // --- Get benchmarks ---

    [Benchmark(Baseline = true)]
    public byte[]? Get_Unencrypted()
    {
        return _plainEngine.Get(_keys[42]);
    }

    [Benchmark]
    public byte[]? Get_Encrypted_CorrectScope()
    {
        _provider.CurrentScope = "tenant:acme";
        return _encryptedEngine.Get(_keys[42]);
    }

    [Benchmark]
    public byte[]? Get_Encrypted_WrongScope()
    {
        _provider.CurrentScope = "tenant:contoso";
        return _encryptedEngine.Get(_keys[42]);
    }

    // --- Set benchmarks ---

    [Benchmark]
    public void Set_Unencrypted()
    {
        _plainEngine.Set(_keys[42], _payload);
    }

    [Benchmark]
    public void Set_Encrypted()
    {
        _encryptedEngine.Set(_keys[42], _payload, _scopedOptions);
    }

    // --- Multi-scope benchmark ---

    [Benchmark]
    [BenchmarkCategory("MultiScope")]
    public void Set_10Scopes()
    {
        for (int i = 0; i < 10; i++)
        {
            var opts = new CacheEntryOptions { Scope = $"tenant:{i}" };
            _encryptedEngine.Set($"ms:{i}", _payload, opts);
        }
    }

    internal sealed class FakeEntitlementProvider : IEntitlementProvider
    {
        public string? CurrentScope { get; set; }
        public string? GetScope() => CurrentScope;
    }
}
