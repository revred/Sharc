// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Trust;

/// <summary>
/// Evaluates row-level access decisions during table scans.
/// Implementations must be allocation-free on the evaluation path.
/// </summary>
/// <remarks>
/// <para>
/// Plugged into <see cref="SharcDataReader"/> via <c>CursorReaderConfig.RowAccessEvaluator</c>.
/// When null, zero overhead — no per-row checks occur.
/// When set, evaluated after existing filter passes but before the row is decoded.
/// </para>
/// <para>
/// Typical implementations match a column value (e.g., <c>owner_id</c>) against the
/// current agent's identity, enabling multi-agent row isolation with sub-microsecond
/// per-row cost.
/// </para>
/// </remarks>
public interface IRowAccessEvaluator
{
    /// <summary>
    /// Determines whether the given row should be visible to the current agent.
    /// </summary>
    /// <param name="payload">Raw SQLite record bytes (header + body). Do not retain.</param>
    /// <param name="rowId">The B-tree rowid of the row.</param>
    /// <returns><c>true</c> if the row is accessible; <c>false</c> to skip it.</returns>
    bool CanAccess(ReadOnlySpan<byte> payload, long rowId);
}
