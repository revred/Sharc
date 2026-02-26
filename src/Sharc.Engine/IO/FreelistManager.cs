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

using System.Buffers.Binary;

namespace Sharc.Core.IO;

/// <summary>
/// Manages the SQLite freelist — a linked list of trunk pages that track freed database pages.
/// Each trunk page contains: next-trunk pointer (uint32 at offset 0), leaf count (uint32 at offset 4),
/// and an array of leaf page numbers (uint32[] starting at offset 8).
/// </summary>
internal sealed class FreelistManager
{
    private readonly IWritablePageSource _source;
    private readonly int _pageSize;

    private uint _firstTrunkPage;
    private int _freelistPageCount;

    /// <summary>
    /// The first freelist trunk page number (0 if no freelist).
    /// </summary>
    public uint FirstTrunkPage => _firstTrunkPage;

    /// <summary>
    /// The total number of pages on the freelist (trunks + leaves).
    /// </summary>
    public int FreelistPageCount => _freelistPageCount;

    /// <summary>
    /// Whether the freelist has any free pages available.
    /// </summary>
    public bool HasFreePages => _freelistPageCount > 0;

    public FreelistManager(IWritablePageSource source, int pageSize)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pageSize = pageSize;
    }

    /// <summary>
    /// Initializes the freelist state from the database header values.
    /// </summary>
    public void Initialize(uint firstTrunkPage, int freelistPageCount)
    {
        _firstTrunkPage = firstTrunkPage;
        _freelistPageCount = freelistPageCount;
    }

    /// <summary>
    /// Maximum number of leaf page entries per trunk page.
    /// Each trunk page has 8 bytes of header (next-trunk + count), rest is uint32 leaf entries.
    /// </summary>
    private int MaxLeavesPerTrunk => (_pageSize - 8) / 4;

    /// <summary>
    /// Pushes a freed page onto the freelist.
    /// If the current trunk has room, adds as a leaf. Otherwise, makes the freed page a new trunk.
    /// </summary>
    public void PushFreePage(uint pageNumber)
    {
        if (_firstTrunkPage == 0)
        {
            // No existing trunk — make this page the first trunk (with 0 leaves)
            Span<byte> newTrunk = stackalloc byte[_pageSize];
            newTrunk.Clear();
            // next-trunk = 0, leaf-count = 0
            _source.WritePage(pageNumber, newTrunk);
            _firstTrunkPage = pageNumber;
            _freelistPageCount++;
            return;
        }

        // Read the current trunk to check if it has room
        Span<byte> trunkBuf = stackalloc byte[_pageSize];
        _source.ReadPage(_firstTrunkPage, trunkBuf);

        int leafCount = (int)BinaryPrimitives.ReadUInt32BigEndian(trunkBuf[4..]);

        if (leafCount < MaxLeavesPerTrunk)
        {
            // Trunk has room — add as a new leaf
            BinaryPrimitives.WriteUInt32BigEndian(trunkBuf[(8 + leafCount * 4)..], pageNumber);
            BinaryPrimitives.WriteUInt32BigEndian(trunkBuf[4..], (uint)(leafCount + 1));
            _source.WritePage(_firstTrunkPage, trunkBuf);
            _freelistPageCount++;
        }
        else
        {
            // Trunk is full — make the freed page a new trunk pointing to the old one
            Span<byte> newTrunk = stackalloc byte[_pageSize];
            newTrunk.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(newTrunk, _firstTrunkPage); // next-trunk = old trunk
            // leaf-count = 0
            _source.WritePage(pageNumber, newTrunk);
            _firstTrunkPage = pageNumber;
            _freelistPageCount++;
        }
    }

    /// <summary>
    /// Pops a free page from the freelist. Returns 0 if the freelist is empty.
    /// Prefers leaf pages first; when a trunk has no leaves left, the trunk itself is returned.
    /// </summary>
    public uint PopFreePage()
    {
        if (_freelistPageCount == 0 || _firstTrunkPage == 0)
            return 0;

        // Read current trunk page
        Span<byte> trunkBuf = stackalloc byte[_pageSize];
        _source.ReadPage(_firstTrunkPage, trunkBuf);

        uint nextTrunk = BinaryPrimitives.ReadUInt32BigEndian(trunkBuf);
        int leafCount = (int)BinaryPrimitives.ReadUInt32BigEndian(trunkBuf[4..]);

        if (leafCount > 0)
        {
            // Pop the last leaf page
            int leafOffset = 8 + (leafCount - 1) * 4;
            uint leafPage = BinaryPrimitives.ReadUInt32BigEndian(trunkBuf[leafOffset..]);

            // Update trunk: decrement leaf count
            BinaryPrimitives.WriteUInt32BigEndian(trunkBuf[4..], (uint)(leafCount - 1));
            _source.WritePage(_firstTrunkPage, trunkBuf);

            _freelistPageCount--;
            return leafPage;
        }
        else
        {
            // No leaves — pop the trunk page itself
            uint trunkPage = _firstTrunkPage;
            _firstTrunkPage = nextTrunk;
            _freelistPageCount--;
            return trunkPage;
        }
    }
}
