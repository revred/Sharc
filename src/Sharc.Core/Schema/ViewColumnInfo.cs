// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Schema;

/// <summary>
/// A column reference within a view definition.
/// Captures the source column name, display alias, and ordinal position.
/// </summary>
public sealed class ViewColumnInfo
{
    /// <summary>Original column name from the source table.</summary>
    public required string SourceName { get; init; }

    /// <summary>Alias if AS was used, otherwise same as <see cref="SourceName"/>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Column ordinal in the view's SELECT list (0-based).</summary>
    public required int Ordinal { get; init; }
}
