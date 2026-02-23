// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.IO;

/// <summary>
/// A page source that shadows an underlying source.
/// All writes are stored in a contiguous <see cref="PageArena"/> until committed.
/// Reads check the shadow (dirty) pages first.
/// </summary>
public sealed class ShadowPageSource : IWritablePageSource
{
    private readonly IPageSource _baseSource;
    private readonly Dictionary<uint, int> _dirtySlots = new(8);
    private PageArena? _arena;
    private uint _maxDirtyPage;
    private long _shadowVersion;
    private bool _disposed;

    /// <inheritdoc />
    public ShadowPageSource(IPageSource baseSource)
    {
        _baseSource = baseSource ?? throw new ArgumentNullException(nameof(baseSource));
    }

    /// <inheritdoc />
    public long DataVersion => ((_baseSource as IWritablePageSource)?.DataVersion ?? 0) + Interlocked.Read(ref _shadowVersion);

    /// <inheritdoc />
    public int PageSize => _baseSource.PageSize;

    /// <inheritdoc />
    public int PageCount
    {
        get
        {
            int baseCount = _baseSource.PageCount;
            if (_dirtySlots.Count == 0) return baseCount;
            return Math.Max(baseCount, (int)_maxDirtyPage);
        }
    }

    /// <summary>Number of dirty pages buffered in this shadow source.</summary>
    internal int DirtyPageCount => _dirtySlots.Count;

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirtySlots.TryGetValue(pageNumber, out int slot))
            return _arena!.GetSlot(slot)[..PageSize];
        return _baseSource.GetPage(pageNumber);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirtySlots.TryGetValue(pageNumber, out int _))
            return GetPage(pageNumber).ToArray();
        return _baseSource.GetPageMemory(pageNumber);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirtySlots.TryGetValue(pageNumber, out int slot))
        {
            _arena!.GetSlot(slot)[..PageSize].CopyTo(destination);
            return PageSize;
        }
        return _baseSource.ReadPage(pageNumber, destination);
    }

    /// <inheritdoc />
    public void Invalidate(uint pageNumber)
    {
        _dirtySlots.Remove(pageNumber);
        _baseSource.Invalidate(pageNumber);
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _arena ??= new PageArena(PageSize);

        if (!_dirtySlots.TryGetValue(pageNumber, out int slot))
        {
            var span = _arena.Allocate(out slot);
            span.Clear();
            _dirtySlots[pageNumber] = slot;
        }
        source.CopyTo(_arena.GetSlot(slot));
        if (pageNumber > _maxDirtyPage) _maxDirtyPage = pageNumber;
        Interlocked.Increment(ref _shadowVersion);
    }

    /// <inheritdoc />
    public void Flush()
    {
        // Shadow pages are in memory, nothing to flush to persistence yet.
    }

    /// <summary>Returns the dirty page numbers for journal creation.</summary>
    internal IEnumerable<uint> GetDirtyPageNumbers() => _dirtySlots.Keys;

    /// <summary>
    /// Writes all dirty pages to the target page source in one pass.
    /// </summary>
    internal void WriteDirtyPagesTo(IWritablePageSource target)
    {
        int pageSize = PageSize;
        foreach (var (pageNumber, slot) in _dirtySlots)
        {
            target.WritePage(pageNumber, _arena!.GetSlot(slot)[..pageSize]);
        }
    }

    /// <summary>
    /// Clears all dirty pages (Rollback equivalent).
    /// </summary>
    internal void ClearShadow() => ClearInternal();

    /// <summary>
    /// Clears dirty pages and resets the arena, but keeps the object reusable.
    /// Unlike Dispose, the object can accept new writes after Reset.
    /// Dictionary capacity is preserved to avoid re-allocation.
    /// </summary>
    internal void Reset()
    {
        ClearInternal();
        _disposed = false;
    }

    private void ClearInternal()
    {
        _dirtySlots.Clear();
        _arena?.Reset();
        _maxDirtyPage = 0;
        Interlocked.Exchange(ref _shadowVersion, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dirtySlots.Clear();
        _arena?.Dispose();
        _arena = null;
        _maxDirtyPage = 0;
    }
}
