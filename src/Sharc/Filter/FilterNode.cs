/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;

namespace Sharc;

/// <summary>
/// Delegate for JIT-compiled filter evaluation.
/// Uses raw pointers/spans for maximum performance without virtual dispatch.
/// </summary>
internal delegate bool BakedDelegate(
    ReadOnlySpan<byte> payload, 
    ReadOnlySpan<long> serialTypes, 
    ReadOnlySpan<int> offsets, 
    long rowId);

/// <summary>
/// High-performance filter node that executes a JIT-compiled predicate tree.
/// Implements "Offset Hoisting" — scanning the record header once per row.
/// </summary>
internal sealed class FilterNode : IFilterNode
{
    private readonly BakedDelegate _compiledDelegate;
    private readonly int[] _referencedOrdinals;
    private readonly int _maxOrdinal;

    public FilterNode(BakedDelegate compiledDelegate, HashSet<int> referencedOrdinals)
    {
        _compiledDelegate = compiledDelegate;
        _referencedOrdinals = referencedOrdinals.OrderBy(x => x).ToArray();
        _maxOrdinal = _referencedOrdinals.Length > 0 ? _referencedOrdinals.Max() : -1;
    }

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, 
                        int bodyOffset, long rowId)
    {
        if (_maxOrdinal < 0) return _compiledDelegate(payload, serialTypes, [], rowId);

        // Tier 2: Offset Hoisting
        // Use stackalloc for the offsets buffer to keep it zero-alloc
        Span<int> offsets = stackalloc int[serialTypes.Length];
        // offsets.Clear(); // Redundant: We only read offsets at indices that we explicitly write to in the loop below.
        
        int currentOffset = bodyOffset;
        int refIdx = 0;

        for (int i = 0; i <= _maxOrdinal && i < serialTypes.Length; i++)
        {
            if (refIdx < _referencedOrdinals.Length && i == _referencedOrdinals[refIdx])
            {
                offsets[i] = currentOffset;
                refIdx++;
            }
            
            // Still need to track currentOffset to find subsequent referenced columns
            currentOffset += SerialTypeCodec.GetContentSize(serialTypes[i]);
        }

        return _compiledDelegate(payload, serialTypes, offsets, rowId);
    }
}
