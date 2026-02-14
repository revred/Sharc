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
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _inner = inner;
        _capacity = capacity;
        
        if (capacity > 0)
        {
            _slots = new CacheSlot[capacity];
            _prev = new int[capacity];
            _next = new int[capacity];
            _lookup = new Dictionary<uint, int>(capacity);
            _freeSlots = new Stack<int>(capacity);
            
            for (int i = 0; i < capacity; i++)
            {
                _slots[i].Data = ArrayPool<byte>.Shared.Rent(inner.PageSize);
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
                return _slots[slotIndex].Data.AsSpan(0, PageSize);
            }

            // Miss
            CacheMissCount++;
            int slot;
            
            if (_count < _capacity)
            {
                // Use free slot
                slot = _freeSlots.Pop();
                _count++;
            }
            else
            {
                // Evict tail (LRU)
                slot = _tail;
                var victimPage = _slots[slot].PageNumber;
                _lookup.Remove(victimPage);
                RemoveNode(slot);
                // Note: We don't push to free slots because we immediately reuse it
            }

            // Load data
            _inner.ReadPage(pageNumber, _slots[slot].Data);
            _slots[slot].PageNumber = pageNumber;
            
            // Add to head and lookup
            AddFirst(slot);
            _lookup[pageNumber] = slot;
            
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

    private struct CacheSlot
    {
        public uint PageNumber;
        public byte[] Data;
    }
}