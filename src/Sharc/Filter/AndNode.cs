// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Short-circuit AND combinator. All children must match.
/// Empty children â†’ true (vacuous truth).
/// </summary>
internal sealed class AndNode : IFilterNode
{
    private readonly IFilterNode[] _children;

    internal AndNode(IFilterNode[] children) => _children = children;

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                         int bodyOffset, long rowId)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            if (!_children[i].Evaluate(payload, serialTypes, bodyOffset, rowId))
                return false;
        }
        return true;
    }
}