// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Short-circuit OR combinator. Any child must match.
/// Empty children â†’ false.
/// </summary>
internal sealed class OrNode : IFilterNode
{
    private readonly IFilterNode[] _children;

    internal OrNode(IFilterNode[] children) => _children = children;

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                         int bodyOffset, long rowId)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            if (_children[i].Evaluate(payload, serialTypes, bodyOffset, rowId))
                return true;
        }
        return false;
    }
}