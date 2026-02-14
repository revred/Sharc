// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Evaluates a filter predicate against a raw SQLite record.
/// Implementations must not allocate on the evaluation path.
/// </summary>
internal interface IFilterNode
{
    /// <summary>
    /// Evaluates the filter against raw record data.
    /// </summary>
    /// <param name="payload">Raw SQLite record bytes (header + body).</param>
    /// <param name="serialTypes">Pre-parsed serial types from record header.</param>
    /// <param name="bodyOffset">Byte offset where record body begins.</param>
    /// <param name="rowId">B-tree rowid of the current row.</param>
    /// <returns>True if the row matches the filter.</returns>
    bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                  int bodyOffset, long rowId);
}