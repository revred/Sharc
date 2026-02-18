// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// Contiguous page buffer arena. Rents a single large buffer from ArrayPool
/// and sub-allocates page-sized slots via bump pointer.
/// <see cref="Reset"/> returns to slot 0 without returning the backing buffer.
/// <see cref="Dispose"/> returns the backing buffer to ArrayPool.
/// </summary>
internal sealed class PageArena : IDisposable
{
    private byte[] _buffer;
    private readonly int _pageSize;
    private int _slotCount;
    private int _capacity;

    public PageArena(int pageSize, int initialCapacity = 8)
    {
        _pageSize = pageSize;
        _capacity = initialCapacity;
        _buffer = ArrayPool<byte>.Shared.Rent(pageSize * initialCapacity);
    }

    /// <summary>Number of allocated slots.</summary>
    public int SlotCount => _slotCount;

    /// <summary>
    /// Allocates the next available page slot. Grows the backing buffer if needed.
    /// Returns a span for the slot and the slot index.
    /// </summary>
    public Span<byte> Allocate(out int slotIndex)
    {
        if (_slotCount >= _capacity)
            Grow();

        slotIndex = _slotCount++;
        return _buffer.AsSpan(slotIndex * _pageSize, _pageSize);
    }

    /// <summary>Returns the span for an existing slot.</summary>
    public Span<byte> GetSlot(int slotIndex)
        => _buffer.AsSpan(slotIndex * _pageSize, _pageSize);

    /// <summary>
    /// Resets the bump pointer to 0. The backing buffer is retained for reuse.
    /// </summary>
    public void Reset()
    {
        _slotCount = 0;
    }

    /// <summary>Returns the backing buffer to ArrayPool.</summary>
    public void Dispose()
    {
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
        }
    }

    private void Grow()
    {
        int newCapacity = _capacity * 2;
        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity * _pageSize);
        _buffer.AsSpan(0, _slotCount * _pageSize).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
        _capacity = newCapacity;
    }
}
