/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
    private readonly Dictionary<uint, LinkedListNode<CacheEntry>> _lookup;
    private readonly LinkedList<CacheEntry> _lru;
    private readonly object _syncRoot = new();
    private bool _disposed;

    /// <inheritdoc />
    public int PageSize => _inner.PageSize;

    /// <inheritdoc />
    public int PageCount => _inner.PageCount;

    /// <summary>Number of cache hits since creation.</summary>
    public int CacheHitCount { get; private set; }

    /// <summary>Number of cache misses since creation.</summary>
    public int CacheMissCount { get; private set; }

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
        _lookup = new Dictionary<uint, LinkedListNode<CacheEntry>>(Math.Min(capacity, 256));
        _lru = new LinkedList<CacheEntry>();
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_capacity == 0)
            return _inner.GetPage(pageNumber);

        lock (_syncRoot)
        {
            if (_lookup.TryGetValue(pageNumber, out var node))
            {
                // Cache hit â€” move to front
                _lru.Remove(node);
                _lru.AddFirst(node);
                CacheHitCount++;
                return node.Value.Data.AsSpan(0, PageSize);
            }

            // Cache miss â€” load from inner source
            CacheMissCount++;
            var buffer = ArrayPool<byte>.Shared.Rent(PageSize);
            _inner.ReadPage(pageNumber, buffer);

            var entry = new CacheEntry(pageNumber, buffer);
            var newNode = _lru.AddFirst(entry);
            _lookup[pageNumber] = newNode;

            // Evict LRU if over capacity
            if (_lru.Count > _capacity)
            {
                var victim = _lru.Last!;
                _lookup.Remove(victim.Value.PageNumber);
                ArrayPool<byte>.Shared.Return(victim.Value.Data);
                _lru.RemoveLast();
            }

            return buffer.AsSpan(0, PageSize);
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
            // Update cache if page exists
            if (_lookup.TryGetValue(pageNumber, out var node))
            {
                source.CopyTo(node.Value.Data);
            }

            // Write through to inner source if it supports writes
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inner is IWritablePageSource writable)
        {
            writable.Flush();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_syncRoot)
        {
            foreach (var node in _lru)
                ArrayPool<byte>.Shared.Return(node.Data);

            _lru.Clear();
            _lookup.Clear();
        }

        _inner.Dispose();
    }

    private readonly struct CacheEntry(uint pageNumber, byte[] data)
    {
        public uint PageNumber { get; } = pageNumber;
        public byte[] Data { get; } = data;
    }
}
