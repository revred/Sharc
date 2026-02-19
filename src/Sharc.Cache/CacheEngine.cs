// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sharc.Cache;

/// <summary>
/// High-performance in-memory cache engine with TTL, sliding expiry, and LRU eviction.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/> for key-value ops
/// and a lock-guarded LRU list for eviction ordering.
/// </summary>
internal sealed class CacheEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly CacheOptions _options;
    private readonly TimeProvider _timeProvider;

    // LRU tracking: MRU at head, LRU at tail. Guarded by _evictionLock.
    private readonly LinkedList<string> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruLookup = new(StringComparer.Ordinal);
    private readonly object _evictionLock = new();

    // Tag reverse index: tag → set of keys. Guarded by _evictionLock.
    private readonly Dictionary<string, HashSet<string>> _keysByTag = new(StringComparer.Ordinal);

    // Entitlement encryption (null when disabled).
    private readonly EntitlementEncryptor? _encryptor;
    private readonly IEntitlementProvider _entitlementProvider;

    private long _currentSize;
    private Timer? _sweepTimer;
    private bool _disposed;

    public CacheEngine(CacheOptions options)
        : this(options, null)
    {
    }

    public CacheEngine(CacheOptions options, IEntitlementProvider? entitlementProvider)
    {
        _options = options;
        _timeProvider = options.TimeProvider;
        _entitlementProvider = entitlementProvider ?? NullEntitlementProvider.Instance;

        if (options.EnableEntitlement)
        {
            var masterKey = options.MasterKeyProvider?.Invoke() ?? options.MasterKey ?? ResolveEnvVarKey();
            if (masterKey is null)
                throw new ArgumentException("MasterKey, MasterKeyProvider, or SHARC_CACHE_KEY environment variable is required when EnableEntitlement is true.", nameof(options));
            _encryptor = new EntitlementEncryptor(masterKey);
            // Zero the source key — EntitlementEncryptor clones it internally.
            if (options.MasterKey is not null)
                CryptographicOperations.ZeroMemory(options.MasterKey);
        }

        if (options.SweepInterval > TimeSpan.Zero)
        {
            _sweepTimer = new Timer(
                _ => SweepExpired(),
                null,
                options.SweepInterval,
                options.SweepInterval);
        }
    }

    /// <summary>Retrieves a cached value by key. Returns null on miss or expiry.</summary>
    public byte[]? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_entries.TryGetValue(key, out var entry))
            return null;

        var now = _timeProvider.GetUtcNow();
        if (entry.IsExpired(now))
        {
            RemoveInternal(key);
            return null;
        }

        entry.Touch(now);
        PromoteToHead(key);
        return DecryptIfScoped(entry, key);
    }

    /// <summary>Stores a value with optional expiration. Overwrites existing entries.</summary>
    public void Set(string key, byte[] value, CacheEntryOptions? entryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var now = _timeProvider.GetUtcNow();

        DateTimeOffset? absoluteExpiration = entryOptions?.AbsoluteExpiration;
        if (entryOptions?.AbsoluteExpirationRelativeToNow is { } relative)
        {
            var computed = now + relative;
            absoluteExpiration = absoluteExpiration.HasValue
                ? (computed < absoluteExpiration.Value ? computed : absoluteExpiration.Value)
                : computed;
        }

        var scope = _encryptor != null ? entryOptions?.Scope : null;
        var storedValue = scope != null ? _encryptor!.Encrypt(value, scope, key) : value;

        var entry = new CacheEntry(storedValue, absoluteExpiration, entryOptions?.SlidingExpiration, now,
                                   tags: entryOptions?.Tags, scope: scope);

        lock (_evictionLock)
        {
            // Remove old entry size and tags if overwriting
            if (_entries.TryGetValue(key, out var existing))
            {
                _currentSize -= existing.Size;
                UnregisterTags(key, existing.Tags);
                if (_lruLookup.TryGetValue(key, out var existingNode))
                {
                    _lruList.Remove(existingNode);
                    _lruLookup.Remove(key);
                }
            }

            _entries[key] = entry;
            _currentSize += entry.Size;
            RegisterTags(key, entry.Tags);

            var node = _lruList.AddFirst(key);
            _lruLookup[key] = node;

            EvictIfNeeded();
        }
    }

    /// <summary>Removes a cached entry. Returns true if the key existed.</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RemoveInternal(key);
    }

    /// <summary>Refreshes the sliding window for an entry without returning its value.</summary>
    public void Refresh(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_entries.TryGetValue(key, out var entry))
            return;

        var now = _timeProvider.GetUtcNow();
        if (entry.IsExpired(now))
        {
            RemoveInternal(key);
            return;
        }

        entry.Touch(now);
        PromoteToHead(key);
    }

    /// <summary>Number of entries currently in the cache.</summary>
    public int GetCount() => _entries.Count;

    /// <summary>Total estimated size of all cached entries in bytes.</summary>
    public long GetSize()
    {
        lock (_evictionLock)
        {
            return _currentSize;
        }
    }

    /// <summary>
    /// Scans all entries and removes expired ones. Called by the background timer
    /// and exposed internally for deterministic testing.
    /// </summary>
    internal void SweepExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _entries.Keys)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.IsExpired(now))
            {
                RemoveInternal(key);
            }
        }
    }

    /// <summary>
    /// Retrieves multiple cached values in a single lock acquisition.
    /// Returns a dictionary with null values for misses and expired entries.
    /// </summary>
    public Dictionary<string, byte[]?> GetMany(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var now = _timeProvider.GetUtcNow();

        lock (_evictionLock)
        {
            foreach (var key in keys)
            {
                if (!_entries.TryGetValue(key, out var entry) || entry.IsExpired(now))
                {
                    result[key] = null;
                    continue;
                }

                entry.Touch(now);
                if (_lruLookup.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                }

                result[key] = DecryptIfScoped(entry, key);
            }
        }

        // Clean up expired entries outside the lock
        foreach (var kvp in result)
        {
            if (kvp.Value is null && _entries.TryGetValue(kvp.Key, out var entry) && entry.IsExpired(now))
                RemoveInternal(kvp.Key);
        }

        return result;
    }

    /// <summary>
    /// Stores multiple entries in a single lock acquisition. Eviction runs once at the end.
    /// </summary>
    public void SetMany(IEnumerable<KeyValuePair<string, byte[]>> entries, CacheEntryOptions? options = null)
    {
        var now = _timeProvider.GetUtcNow();

        DateTimeOffset? absoluteExpiration = options?.AbsoluteExpiration;
        if (options?.AbsoluteExpirationRelativeToNow is { } relative)
        {
            var computed = now + relative;
            absoluteExpiration = absoluteExpiration.HasValue
                ? (computed < absoluteExpiration.Value ? computed : absoluteExpiration.Value)
                : computed;
        }

        var scope = _encryptor != null ? options?.Scope : null;

        lock (_evictionLock)
        {
            foreach (var kvp in entries)
            {
                var storedValue = scope != null ? _encryptor!.Encrypt(kvp.Value, scope, kvp.Key) : kvp.Value;
                var entry = new CacheEntry(storedValue, absoluteExpiration, options?.SlidingExpiration, now,
                                           tags: options?.Tags, scope: scope);

                if (_entries.TryGetValue(kvp.Key, out var existing))
                {
                    _currentSize -= existing.Size;
                    UnregisterTags(kvp.Key, existing.Tags);
                    if (_lruLookup.TryGetValue(kvp.Key, out var existingNode))
                    {
                        _lruList.Remove(existingNode);
                        _lruLookup.Remove(kvp.Key);
                    }
                }

                _entries[kvp.Key] = entry;
                _currentSize += entry.Size;
                RegisterTags(kvp.Key, entry.Tags);

                var node = _lruList.AddFirst(kvp.Key);
                _lruLookup[kvp.Key] = node;
            }

            EvictIfNeeded();
        }
    }

    /// <summary>
    /// Removes multiple entries in a single lock acquisition.
    /// Returns the number of entries that were actually removed.
    /// </summary>
    public int RemoveMany(IEnumerable<string> keys)
    {
        int count = 0;

        lock (_evictionLock)
        {
            foreach (var key in keys)
            {
                if (_entries.TryRemove(key, out var removed))
                {
                    _currentSize -= removed.Size;
                    UnregisterTags(key, removed.Tags);
                    if (_lruLookup.TryGetValue(key, out var node))
                    {
                        _lruList.Remove(node);
                        _lruLookup.Remove(key);
                    }
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Removes all entries associated with the given tag. Returns count removed.</summary>
    public int EvictByTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        lock (_evictionLock)
        {
            if (!_keysByTag.TryGetValue(tag, out var keys))
                return 0;

            int count = 0;
            // Snapshot to avoid modifying collection during iteration
            foreach (var key in keys.ToArray())
            {
                if (_entries.TryRemove(key, out var removed))
                {
                    _currentSize -= removed.Size;
                    if (_lruLookup.TryGetValue(key, out var node))
                    {
                        _lruList.Remove(node);
                        _lruLookup.Remove(key);
                    }
                    // Unregister from ALL tags this entry has, not just the evicting tag
                    UnregisterTags(key, removed.Tags);
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Removes all entries associated with any of the given tags. Returns total count removed.</summary>
    public int EvictByTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        lock (_evictionLock)
        {
            // Collect union of keys across all tags
            var keysToRemove = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                if (_keysByTag.TryGetValue(tag, out var keys))
                {
                    foreach (var key in keys)
                        keysToRemove.Add(key);
                }
            }

            int count = 0;
            foreach (var key in keysToRemove)
            {
                if (_entries.TryRemove(key, out var removed))
                {
                    _currentSize -= removed.Size;
                    if (_lruLookup.TryGetValue(key, out var node))
                    {
                        _lruList.Remove(node);
                        _lruLookup.Remove(key);
                    }
                    UnregisterTags(key, removed.Tags);
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Removes all entries associated with the given entitlement scope. Returns count removed.</summary>
    public int EvictByScope(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        lock (_evictionLock)
        {
            // Collect keys that match the scope
            var keysToRemove = new List<string>();
            foreach (var kvp in _entries)
            {
                if (string.Equals(kvp.Value.Scope, scope, StringComparison.Ordinal))
                    keysToRemove.Add(kvp.Key);
            }

            int count = 0;
            foreach (var key in keysToRemove)
            {
                if (_entries.TryRemove(key, out var removed))
                {
                    _currentSize -= removed.Size;
                    UnregisterTags(key, removed.Tags);
                    if (_lruLookup.TryGetValue(key, out var node))
                    {
                        _lruList.Remove(node);
                        _lruLookup.Remove(key);
                    }
                    count++;
                }
            }

            return count;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sweepTimer?.Dispose();
        _sweepTimer = null;
        _encryptor?.Dispose();
    }

    /// <summary>
    /// If the entry has a scope, attempts to decrypt using the current caller's scope.
    /// Returns null if scopes don't match. Public entries (no scope) return as-is.
    /// </summary>
    private byte[]? DecryptIfScoped(CacheEntry entry, string key)
    {
        if (entry.Scope is null)
            return entry.Value; // public entry

        if (_encryptor is null)
            return entry.Value; // entitlement disabled at engine level

        var currentScope = _entitlementProvider.GetScope();
        if (!string.Equals(currentScope, entry.Scope, StringComparison.Ordinal))
            return null; // scope mismatch → cache miss

        return _encryptor.TryDecrypt(entry.Value, entry.Scope, key);
    }

    private bool RemoveInternal(string key)
    {
        if (!_entries.TryRemove(key, out var removed))
            return false;

        lock (_evictionLock)
        {
            _currentSize -= removed.Size;
            UnregisterTags(key, removed.Tags);
            if (_lruLookup.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruLookup.Remove(key);
            }
        }

        return true;
    }

    private void PromoteToHead(string key)
    {
        lock (_evictionLock)
        {
            if (_lruLookup.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }
    }

    /// <summary>
    /// Evicts LRU entries until the cache is within budget. Must be called under <see cref="_evictionLock"/>.
    /// </summary>
    private void EvictIfNeeded()
    {
        // Evict by size
        while (_currentSize > _options.MaxCacheSize && _lruList.Last != null)
        {
            EvictTail();
        }

        // Evict by entry count
        if (_options.MaxEntries > 0)
        {
            while (_entries.Count > _options.MaxEntries && _lruList.Last != null)
            {
                EvictTail();
            }
        }
    }

    /// <summary>Registers a key's tags in the reverse index. Must be called under _evictionLock.</summary>
    private void RegisterTags(string key, string[]? tags)
    {
        if (tags is null) return;
        foreach (var tag in tags)
        {
            if (!_keysByTag.TryGetValue(tag, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _keysByTag[tag] = set;
            }
            set.Add(key);
        }
    }

    /// <summary>Unregisters a key from all its tag sets. Must be called under _evictionLock.</summary>
    private void UnregisterTags(string key, string[]? tags)
    {
        if (tags is null) return;
        foreach (var tag in tags)
        {
            if (_keysByTag.TryGetValue(tag, out var set))
            {
                set.Remove(key);
                if (set.Count == 0)
                    _keysByTag.Remove(tag);
            }
        }
    }

    private void EvictTail()
    {
        var victimNode = _lruList.Last!;
        var victimKey = victimNode.Value;

        _lruList.Remove(victimNode);
        _lruLookup.Remove(victimKey);

        if (_entries.TryRemove(victimKey, out var removed))
        {
            _currentSize -= removed.Size;
            UnregisterTags(victimKey, removed.Tags);
        }
    }

    /// <summary>
    /// Attempts to read a base64-encoded master key from the SHARC_CACHE_KEY environment variable.
    /// Returns null if the variable is not set. Throws on invalid base64 or wrong key length.
    /// </summary>
    private static byte[]? ResolveEnvVarKey()
    {
        var envValue = Environment.GetEnvironmentVariable("SHARC_CACHE_KEY");
        if (string.IsNullOrEmpty(envValue))
            return null;

        return Convert.FromBase64String(envValue);
    }
}
