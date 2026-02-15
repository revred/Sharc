// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A single node in a flat predicate tree.
/// Leaf nodes have <see cref="ColumnName"/> and <see cref="Value"/>.
/// Logical nodes (<see cref="IntentOp.And"/>, <see cref="IntentOp.Or"/>, <see cref="IntentOp.Not"/>)
/// reference children by index.
/// </summary>
public struct PredicateNode
{
    /// <summary>Operation for this node.</summary>
    public required IntentOp Op { get; init; }

    /// <summary>Column name (leaf nodes only).</summary>
    public string? ColumnName { get; init; }

    /// <summary>Comparison value (leaf nodes only).</summary>
    public IntentValue Value { get; init; }

    /// <summary>Upper bound for <see cref="IntentOp.Between"/>.</summary>
    public IntentValue HighValue { get; init; }

    /// <summary>Left child index, or -1 for leaf nodes.</summary>
    public int LeftIndex { get; init; } = -1;

    /// <summary>Right child index, or -1 for leaf/unary nodes.</summary>
    public int RightIndex { get; init; } = -1;

    /// <summary>Initializes default field values.</summary>
    public PredicateNode() { }
}

/// <summary>
/// Flat array of <see cref="PredicateNode"/> in post-order layout.
/// Children appear before their parents. The root is at <see cref="RootIndex"/>.
/// </summary>
public readonly struct PredicateIntent
{
    /// <summary>Post-order node array.</summary>
    public PredicateNode[] Nodes { get; }

    /// <summary>Index of the root node in <see cref="Nodes"/>.</summary>
    public int RootIndex { get; }

    /// <summary>Creates a predicate intent from a node array and root index.</summary>
    public PredicateIntent(PredicateNode[] nodes, int rootIndex)
    {
        Nodes = nodes;
        RootIndex = rootIndex;
    }
}
