// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;

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

    private long _currentSize;
    private Timer? _sweepTimer;
    private bool _disposed;

    public CacheEngine(CacheOptions options)
    {
        _options = options;
        _timeProvider = options.TimeProvider;

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
        return entry.Value;
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

        var entry = new CacheEntry(value, absoluteExpiration, entryOptions?.SlidingExpiration, now);

        lock (_evictionLock)
        {
            // Remove old entry size if overwriting
            if (_entries.TryGetValue(key, out var existing))
            {
                _currentSize -= existing.Size;
                if (_lruLookup.TryGetValue(key, out var existingNode))
                {
                    _lruList.Remove(existingNode);
                    _lruLookup.Remove(key);
                }
            }

            _entries[key] = entry;
            _currentSize += entry.Size;

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

                result[key] = entry.Value;
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

        lock (_evictionLock)
        {
            foreach (var kvp in entries)
            {
                var entry = new CacheEntry(kvp.Value, absoluteExpiration, options?.SlidingExpiration, now);

                if (_entries.TryGetValue(kvp.Key, out var existing))
                {
                    _currentSize -= existing.Size;
                    if (_lruLookup.TryGetValue(kvp.Key, out var existingNode))
                    {
                        _lruList.Remove(existingNode);
                        _lruLookup.Remove(kvp.Key);
                    }
                }

                _entries[kvp.Key] = entry;
                _currentSize += entry.Size;

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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sweepTimer?.Dispose();
        _sweepTimer = null;
    }

    private bool RemoveInternal(string key)
    {
        if (!_entries.TryRemove(key, out var removed))
            return false;

        lock (_evictionLock)
        {
            _currentSize -= removed.Size;
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

    private void EvictTail()
    {
        var victimNode = _lruList.Last!;
        var victimKey = victimNode.Value;

        _lruList.Remove(victimNode);
        _lruLookup.Remove(victimKey);

        if (_entries.TryRemove(victimKey, out var removed))
        {
            _currentSize -= removed.Size;
        }
    }
}
