// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Logical negation. Inverts the child result.
/// </summary>
internal sealed class NotNode : IFilterNode
{
    private readonly IFilterNode _child;

    internal NotNode(IFilterNode child) => _child = child;

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                         int bodyOffset, long rowId)
    {
        return !_child.Evaluate(payload, serialTypes, bodyOffset, rowId);
    }
}