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
