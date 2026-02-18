// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Represents a resolved JOIN operation.
/// </summary>
public sealed class JoinIntent
{
    /// <summary>The type of join (Inner, Left, etc.).</summary>
    public required JoinType Kind { get; init; }

    /// <summary>The name of the table being joined.</summary>
    public required string TableName { get; init; }

    /// <summary>The alias of the table being joined, if any.</summary>
    public required string? TableAlias { get; init; }

    /// <summary>The left column reference (e.g. "u.id").</summary>
    public string? LeftColumn { get; init; }

    /// <summary>The right column reference (e.g. "o.user_id").</summary>
    public string? RightColumn { get; init; }
}

/// <summary>
/// The type of join operation.
/// </summary>
public enum JoinType : byte
{
    /// <summary>Inner Join.</summary>
    Inner,
    /// <summary>Left Outer Join.</summary>
    Left,
    /// <summary>Right Outer Join.</summary>
    Right,
    /// <summary>Cross Join.</summary>
    Cross
}
