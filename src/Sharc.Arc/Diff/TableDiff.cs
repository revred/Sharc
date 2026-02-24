// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Diff;

/// <summary>
/// Row-level data diff for a single table. Uses streaming merge-join on rowid
/// with <c>Fingerprint128</c> comparison for O(N+M) time, O(1) memory.
/// </summary>
public sealed class TableDiff
{
    /// <summary>Name of the table being compared.</summary>
    public required string TableName { get; init; }

    /// <summary>True if the table has identical data in both arcs.</summary>
    public bool IsIdentical => ModifiedRowCount == 0
        && LeftOnlyRowCount == 0
        && RightOnlyRowCount == 0;

    /// <summary>Number of rows present in the left arc's table.</summary>
    public required long LeftRowCount { get; init; }

    /// <summary>Number of rows present in the right arc's table.</summary>
    public required long RightRowCount { get; init; }

    /// <summary>Number of rows with matching rowid and identical fingerprints.</summary>
    public required long MatchingRowCount { get; init; }

    /// <summary>Number of rows with matching rowid but different fingerprints.</summary>
    public required long ModifiedRowCount { get; init; }

    /// <summary>Number of rows only in the left arc (rowid not in right).</summary>
    public required long LeftOnlyRowCount { get; init; }

    /// <summary>Number of rows only in the right arc (rowid not in left).</summary>
    public required long RightOnlyRowCount { get; init; }

    /// <summary>True if the diff was truncated due to <see cref="ArcDiffOptions.MaxRowDiffsPerTable"/>.</summary>
    public bool Truncated { get; init; }
}
