// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// LRU-cached wrapper around any <see cref="IPageSource"/>.
/// Rents buffers from <see cref="ArrayPool{T}.Shared"/> and returns them on eviction or dispose.
/// </summary>
public sealed class CachedPageSource : IWritablePageSource
{
    private readonly IPageSource _inner;
    private readonly int _capacity;
    private readonly PrefetchOptions? _prefetchOptions;
    private uint _lastAccessedPage;
    private int _sequentialCount;
    private bool _disposed;

    // Zero-allocation LRU Implementation
    // We use a fixed-size array of entries and intrusive double-linked list using item indices.
    // _entries[i] holds the data and metadata for slot i.
    // _pageLookup maps PageNumber -> SlotIndex.
    
    private readonly CacheSlot[] _slots;
    
    // Intrusive list pointers (indices)
    private readonly int[] _prev;
    private readonly int[] _next;
    
    // Dictionary for fast lookup. 
    // Note: Dictionary<uint, int> does not allocate on Add/Remove if capacity is sufficient 
    // and we reuse the same keys (page numbers). Effectively it stabilizes.
    // For absolute zero-alloc, we could use a specialized IntMap, but standard Dictionary is usually fine
    // after warm-up. Given we only store int, it's efficient.
    private readonly Dictionary<uint, int> _lookup;
    
    // List head/tail identifiers
    private int _head;
    private int _tail;
    private int _count;
    private readonly Stack<int> _freeSlots; // Stores indices of unused slots
    private readonly object _syncRoot = new();

    /// <summary>Number of cache hits since creation.</summary>
    public int CacheHitCount { get; private set; }

    /// <summary>Number of cache misses since creation.</summary>
    public int CacheMissCount { get; private set; }

    /// <summary>Number of slots that have had a buffer rented (demand-driven). For test observability.</summary>
    internal int AllocatedSlotCount { get; private set; }

    /// <inheritdoc />
    public int PageSize => _inner.PageSize;

    /// <inheritdoc />
    public int PageCount => _inner.PageCount;

    /// <summary>
    /// Wraps an inner page source with an LRU cache of the given capacity.
    /// </summary>
    /// <param name="inner">The underlying page source.</param>
    /// <param name="capacity">Maximum number of pages to cache. 0 disables caching.</param>
    public CachedPageSource(IPageSource inner, int capacity)
        : this(inner, capacity, prefetchOptions: null)
    {
    }

    /// <summary>
    /// Wraps an inner page source with an LRU cache and optional sequential read-ahead prefetch.
    /// </summary>
    /// <param name="inner">The underlying page source.</param>
    /// <param name="capacity">Maximum number of pages to cache. 0 disables caching.</param>
    /// <param name="prefetchOptions">Optional prefetch configuration. Null disables prefetch.</param>
    public CachedPageSource(IPageSource inner, int capacity, PrefetchOptions? prefetchOptions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _inner = inner;
        _capacity = capacity;
        _prefetchOptions = prefetchOptions;
        
        if (capacity > 0)
        {
            _slots = new CacheSlot[capacity];
            _prev = new int[capacity];
            _next = new int[capacity];
            _lookup = new Dictionary<uint, int>(capacity);
            _freeSlots = new Stack<int>(capacity);
            
            // Demand-driven: only slot metadata is pre-allocated.
            // Capacity is a maximum, not a reservation.
            // Page buffers (byte[]) are rented on first use in AllocateSlot().
            for (int i = 0; i < capacity; i++)
            {
                _freeSlots.Push(i);
            }
            
            _head = -1;
            _tail = -1;
        }
        else
        {
            _slots = Array.Empty<CacheSlot>();
            _prev = Array.Empty<int>();
            _next = Array.Empty<int>();
            _lookup = new Dictionary<uint, int>();
            _freeSlots = new Stack<int>();
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_capacity == 0)
            return _inner.GetPage(pageNumber);

        lock (_syncRoot)
        {
            if (_lookup.TryGetValue(pageNumber, out int slotIndex))
            {
                // Hit
                MoveToHead(slotIndex);
                CacheHitCount++;
                UpdateSequentialTracking(pageNumber);
                return _slots[slotIndex].Data.AsSpan(0, PageSize);
            }

            // Miss
            CacheMissCount++;
            int slot = AllocateSlot();

            // Load data
            _inner.ReadPage(pageNumber, _slots[slot].Data);
            _slots[slot].PageNumber = pageNumber;

            // Add to head and lookup
            AddFirst(slot);
            _lookup[pageNumber] = slot;

            // Sequential access detection and prefetch
            TryPrefetch(pageNumber);

            return _slots[slot].Data.AsSpan(0, PageSize);
        }
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        GetPage(pageNumber).CopyTo(destination);
        return PageSize;
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncRoot)
        {
            // Update cache if present
            if (_capacity > 0 && _lookup.TryGetValue(pageNumber, out int slot))
            {
                source.CopyTo(_slots[slot].Data);
                MoveToHead(slot);
            }

            // A write breaks sequential read-ahead patterns.
            _sequentialCount = 0;

            // Write through
            if (_inner is IWritablePageSource writable)
            {
                writable.WritePage(pageNumber, source);
            }
            else
            {
                throw new NotSupportedException("Underlying page source does not support writes.");
            }
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (_inner is IWritablePageSource writable)
            writable.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_capacity > 0)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Data != null)
                {
                    ArrayPool<byte>.Shared.Return(_slots[i].Data);
                    _slots[i].Data = null!;
                }
            }
        }
        
        _inner.Dispose();
    }

    // --- Linked List Helpers ---

    private void MoveToHead(int slot)
    {
        if (slot == _head) return;
        RemoveNode(slot);
        AddFirst(slot);
    }

    private void AddFirst(int slot)
    {
        if (_head == -1)
        {
            _head = slot;
            _tail = slot;
            _prev[slot] = -1;
            _next[slot] = -1;
        }
        else
        {
            _prev[slot] = -1;
            _next[slot] = _head;
            _prev[_head] = slot;
            _head = slot;
        }
    }

    private void RemoveNode(int slot)
    {
        int p = _prev[slot];
        int n = _next[slot];

        if (p != -1) _next[p] = n;
        else _head = n;

        if (n != -1) _prev[n] = p;
        else _tail = p;
        
        _prev[slot] = -1;
        _next[slot] = -1;
    }

    // --- Prefetch Helpers ---

    /// <summary>Allocates a cache slot, evicting LRU if full. Must be called under _syncRoot.</summary>
    private int AllocateSlot()
    {
        if (_count < _capacity)
        {
            int slot = _freeSlots.Pop();
            _count++;
            // Demand-driven: rent buffer on first use.
            // Capacity is a maximum, not a reservation.
            if (_slots[slot].Data is null)
            {
                _slots[slot].Data = ArrayPool<byte>.Shared.Rent(_inner.PageSize);
                AllocatedSlotCount++;
            }
            return slot;
        }
        else
        {
            // Eviction: reuse the existing buffer from the LRU tail (already rented).
            int slot = _tail;
            var victimPage = _slots[slot].PageNumber;
            _lookup.Remove(victimPage);
            RemoveNode(slot);
            return slot;
        }
    }

    /// <summary>Updates sequential access tracking. Must be called under _syncRoot.</summary>
    private void UpdateSequentialTracking(uint pageNumber)
    {
        if (pageNumber == _lastAccessedPage + 1)
            _sequentialCount++;
        else
            _sequentialCount = 1;

        _lastAccessedPage = pageNumber;
    }

    /// <summary>
    /// Detects sequential access patterns and prefetches ahead. Must be called under _syncRoot.
    /// </summary>
    private void TryPrefetch(uint pageNumber)
    {
        UpdateSequentialTracking(pageNumber);

        if (_prefetchOptions is null || _prefetchOptions.Disabled)
            return;

        if (_sequentialCount < _prefetchOptions.SequentialThreshold)
            return;

        uint pageCount = (uint)PageCount;
        for (int i = 1; i <= _prefetchOptions.PrefetchDepth; i++)
        {
            uint prefetchPage = pageNumber + (uint)i;
            if (prefetchPage > pageCount)
                break;

            if (_lookup.ContainsKey(prefetchPage))
                continue;

            CacheMissCount++;
            int slot = AllocateSlot();

            _inner.ReadPage(prefetchPage, _slots[slot].Data);
            _slots[slot].PageNumber = prefetchPage;

            AddFirst(slot);
            _lookup[prefetchPage] = slot;
        }
    }

    private struct CacheSlot
    {
        public uint PageNumber;
        public byte[] Data;
    }
}