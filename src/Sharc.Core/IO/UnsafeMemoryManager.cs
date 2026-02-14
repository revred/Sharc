// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// Wraps an unmanaged memory region as a <see cref="MemoryManager{T}"/> so it can be
/// consumed as <see cref="Memory{T}"/> and <see cref="Span{T}"/> by safe code.
/// This is the standard BCL pattern used by ASP.NET Core / Kestrel for zero-copy I/O.
/// The caller is responsible for ensuring the underlying memory outlives this instance.
/// </summary>
internal sealed unsafe class UnsafeMemoryManager : MemoryManager<byte>
{
    private byte* _pointer;
    private readonly int _length;

    internal UnsafeMemoryManager(byte* pointer, int length)
    {
        _pointer = pointer;
        _length = length;
    }

    /// <inheritdoc />
    public override Span<byte> GetSpan() => new(_pointer, _length);

    /// <inheritdoc />
    public override MemoryHandle Pin(int elementIndex = 0) =>
        new(_pointer + elementIndex);

    /// <inheritdoc />
    public override void Unpin() { /* Memory-mapped region is always pinned by the OS. */ }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => _pointer = null;
}