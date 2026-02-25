/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.Primitives;
using System.Buffers;

namespace Sharc;

/// <summary>
/// Delegate for closure-composed filter evaluation.
/// Uses raw spans for maximum performance without virtual dispatch.
/// </summary>
internal delegate bool BakedDelegate(
    ReadOnlySpan<byte> payload, 
    ReadOnlySpan<long> serialTypes, 
    ReadOnlySpan<int> offsets, 
    long rowId);

/// <summary>
/// High-performance filter node that executes a closure-composed predicate chain.
/// Implements "Offset Hoisting" — scanning the record header once per row.
/// </summary>
internal sealed class FilterNode : IFilterNode
{
    private readonly BakedDelegate _compiledDelegate;
    private readonly int[] _referencedOrdinals;
    private readonly int _maxOrdinal;

    internal BakedDelegate CompiledDelegate => _compiledDelegate;
    internal int[] ReferencedOrdinals => _referencedOrdinals;

    public FilterNode(BakedDelegate compiledDelegate, HashSet<int> referencedOrdinals)
    {
        _compiledDelegate = compiledDelegate;
        _referencedOrdinals = new int[referencedOrdinals.Count];
        referencedOrdinals.CopyTo(_referencedOrdinals);
        Array.Sort(_referencedOrdinals);
        _maxOrdinal = _referencedOrdinals.Length == 0 ? -1 : _referencedOrdinals[^1];
    }

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, 
                        int bodyOffset, long rowId)
    {
        if (_maxOrdinal < 0) return _compiledDelegate(payload, serialTypes, [], rowId);

        int maxTrackedOrdinal = _maxOrdinal;
        if (maxTrackedOrdinal >= serialTypes.Length)
            maxTrackedOrdinal = serialTypes.Length - 1;
        if (maxTrackedOrdinal < 0)
            return _compiledDelegate(payload, serialTypes, [], rowId);

        int neededOffsets = maxTrackedOrdinal + 1;
        int[]? pooled = null;
        Span<int> offsets = neededOffsets <= 256
            ? stackalloc int[neededOffsets]
            : (pooled = ArrayPool<int>.Shared.Rent(neededOffsets)).AsSpan(0, neededOffsets);

        int currentOffset = bodyOffset;
        int refIdx = 0;

        try
        {
            for (int i = 0; i <= maxTrackedOrdinal; i++)
            {
                if (refIdx < _referencedOrdinals.Length && i == _referencedOrdinals[refIdx])
                {
                    offsets[i] = currentOffset;
                    refIdx++;
                }

                currentOffset += SerialTypeCodec.GetContentSize(serialTypes[i]);
            }

            return _compiledDelegate(payload, serialTypes, offsets, rowId);
        }
        finally
        {
            if (pooled != null)
                ArrayPool<int>.Shared.Return(pooled, clearArray: false);
        }
    }
}
