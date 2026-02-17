// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CacheEngineEntitlementTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeEntitlementProvider _provider = new();
    private readonly byte[] _masterKey;
    private readonly CacheEngine _engine;

    public CacheEngineEntitlementTests()
    {
        _masterKey = new byte[32];
        RandomNumberGenerator.Fill(_masterKey);
        _engine = CreateEngine(enableEntitlement: true);
    }

    public void Dispose() => _engine.Dispose();

    private CacheEngine CreateEngine(bool enableEntitlement = true, long maxSize = 256L * 1024 * 1024)
    {
        return new CacheEngine(
            new CacheOptions
            {
                TimeProvider = _time,
                SweepInterval = TimeSpan.Zero,
                MaxCacheSize = maxSize,
                EnableEntitlement = enableEntitlement,
                MasterKey = enableEntitlement ? _masterKey : null,
            },
            _provider);
    }

    [Fact]
    public void Set_WithScope_EncryptsValue()
    {
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        _provider.CurrentScope = "tenant:acme";
        _engine.Set("k1", plaintext, new CacheEntryOptions { Scope = "tenant:acme" });

        // The stored value should differ from plaintext (it's encrypted)
        // We verify this by getting with wrong scope → null
        _provider.CurrentScope = "wrong";
        Assert.Null(_engine.Get("k1"));
    }

    [Fact]
    public void Get_CorrectScope_DecryptsValue()
    {
        var plaintext = new byte[] { 10, 20, 30 };
        _engine.Set("k1", plaintext, new CacheEntryOptions { Scope = "tenant:acme" });

        _provider.CurrentScope = "tenant:acme";
        var result = _engine.Get("k1");

        Assert.NotNull(result);
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void Get_WrongScope_ReturnsNull()
    {
        var plaintext = new byte[] { 42 };
        _engine.Set("k1", plaintext, new CacheEntryOptions { Scope = "tenant:acme" });

        _provider.CurrentScope = "tenant:contoso";
        Assert.Null(_engine.Get("k1"));
    }

    [Fact]
    public void Get_PublicEntry_ReadableByAnyScope()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        _engine.Set("k1", plaintext); // no scope → public

        _provider.CurrentScope = "tenant:acme";
        Assert.Equal(plaintext, _engine.Get("k1"));

        _provider.CurrentScope = "tenant:contoso";
        Assert.Equal(plaintext, _engine.Get("k1"));

        _provider.CurrentScope = null;
        Assert.Equal(plaintext, _engine.Get("k1"));
    }

    [Fact]
    public void Set_PublicEntry_NoEncryption()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        _engine.Set("k1", plaintext); // no scope

        // Should be readable without any scope
        _provider.CurrentScope = null;
        var result = _engine.Get("k1");
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void Set_EntitlementDisabled_NoCryptoOverhead()
    {
        using var engine = CreateEngine(enableEntitlement: false);
        var plaintext = new byte[] { 1, 2, 3 };

        // Scope on entry options should be ignored when entitlement is disabled
        engine.Set("k1", plaintext, new CacheEntryOptions { Scope = "tenant:acme" });

        _provider.CurrentScope = "anything";
        var result = engine.Get("k1");
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void GetMany_MixedScopes_CorrectResults()
    {
        _engine.Set("a", [1], new CacheEntryOptions { Scope = "tenant:x" });
        _engine.Set("b", [2]); // public
        _engine.Set("c", [3], new CacheEntryOptions { Scope = "tenant:y" });

        _provider.CurrentScope = "tenant:x";
        var results = _engine.GetMany(["a", "b", "c"]);

        Assert.Equal(new byte[] { 1 }, results["a"]); // scope matches → decrypted
        Assert.Equal(new byte[] { 2 }, results["b"]); // public → readable
        Assert.Null(results["c"]);                      // scope mismatch → null
    }

    [Fact]
    public void SetMany_WithScope_AllEncrypted()
    {
        var entries = new[]
        {
            new KeyValuePair<string, byte[]>("a", [1]),
            new KeyValuePair<string, byte[]>("b", [2]),
        };

        _engine.SetMany(entries, new CacheEntryOptions { Scope = "tenant:x" });

        // Should be readable with correct scope
        _provider.CurrentScope = "tenant:x";
        Assert.Equal(new byte[] { 1 }, _engine.Get("a"));
        Assert.Equal(new byte[] { 2 }, _engine.Get("b"));

        // Should NOT be readable with wrong scope
        _provider.CurrentScope = "tenant:y";
        Assert.Null(_engine.Get("a"));
        Assert.Null(_engine.Get("b"));
    }

    [Fact]
    public void EvictByScope_RemovesScopedEntries()
    {
        _engine.Set("a1", [1], new CacheEntryOptions { Scope = "tenant:acme" });
        _engine.Set("a2", [2], new CacheEntryOptions { Scope = "tenant:acme" });
        _engine.Set("a3", [3], new CacheEntryOptions { Scope = "tenant:acme" });
        _engine.Set("b1", [4], new CacheEntryOptions { Scope = "tenant:contoso" });
        _engine.Set("b2", [5], new CacheEntryOptions { Scope = "tenant:contoso" });

        int removed = _engine.EvictByScope("tenant:acme");

        Assert.Equal(3, removed);
        Assert.Equal(2, _engine.GetCount());
    }

    [Fact]
    public void EvictByScope_LeavesOtherScopesAlone()
    {
        _engine.Set("a1", [1], new CacheEntryOptions { Scope = "tenant:acme" });
        _engine.Set("b1", [4], new CacheEntryOptions { Scope = "tenant:contoso" });

        _engine.EvictByScope("tenant:acme");

        _provider.CurrentScope = "tenant:contoso";
        Assert.Equal(new byte[] { 4 }, _engine.Get("b1"));
    }

    [Fact]
    public void Get_ExpiredScopedEntry_ReturnsNull()
    {
        _engine.Set("k1", [1, 2, 3], new CacheEntryOptions
        {
            Scope = "tenant:acme",
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        });

        _time.Advance(TimeSpan.FromMinutes(2));

        _provider.CurrentScope = "tenant:acme";
        Assert.Null(_engine.Get("k1"));
    }

    [Fact]
    public void RoundTrip_LargeValue_EncryptDecryptSucceeds()
    {
        var plaintext = new byte[64 * 1024]; // 64 KB
        RandomNumberGenerator.Fill(plaintext);

        _engine.Set("big", plaintext, new CacheEntryOptions { Scope = "scope" });

        _provider.CurrentScope = "scope";
        var result = _engine.Get("big");

        Assert.NotNull(result);
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void Set_EntitlementEnabled_NoScope_StoredAsPlaintext()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        // Entitlement is enabled but no scope on this entry → stored as plaintext
        _engine.Set("k1", plaintext);

        _provider.CurrentScope = null;
        Assert.Equal(plaintext, _engine.Get("k1"));

        _provider.CurrentScope = "any";
        Assert.Equal(plaintext, _engine.Get("k1"));
    }

    internal sealed class FakeEntitlementProvider : IEntitlementProvider
    {
        public string? CurrentScope { get; set; }
        public string? GetScope() => CurrentScope;
    }
}
