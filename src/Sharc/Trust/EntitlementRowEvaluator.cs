// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Records;

namespace Sharc.Trust;

/// <summary>
/// Concrete <see cref="IRowAccessEvaluator"/> that filters rows by entitlement tag.
/// <para>
/// <b>Zero cost when not used</b>: this evaluator is only instantiated when entitlement
/// filtering is explicitly enabled via <c>CursorReaderConfig.RowAccessEvaluator</c>.
/// When null, the <c>ProcessRow()</c> hot path skips all evaluation (JIT eliminates the branch).
/// </para>
/// <para>
/// Reads the entitlement tag column directly from the raw SQLite record payload using
/// <see cref="RecordDecoder.DecodeStringAt"/>. No row materialization occurs for rejected rows.
/// </para>
/// </summary>
public sealed class EntitlementRowEvaluator : IRowAccessEvaluator
{
    private readonly SharcEntitlementContext _context;
    private readonly int _tagColumnOrdinal;
    private readonly RecordDecoder _decoder;

    // Pre-allocated arrays for serial type parsing — reused across CanAccess calls.
    private readonly long[] _serialTypes;
    private readonly int[] _columnOffsets;

    /// <summary>
    /// Creates an entitlement row evaluator.
    /// </summary>
    /// <param name="context">The caller's entitlement context (set of authorized tags).</param>
    /// <param name="tagColumnOrdinal">The zero-based ordinal of the entitlement tag column in the table.</param>
    /// <param name="columnCount">Total number of columns in the table (for buffer sizing).</param>
    public EntitlementRowEvaluator(SharcEntitlementContext context, int tagColumnOrdinal, int columnCount)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tagColumnOrdinal = tagColumnOrdinal;
        _decoder = new RecordDecoder();

        // Allocate buffers once — reused for every CanAccess call
        _serialTypes = new long[columnCount];
        _columnOffsets = new int[columnCount];
    }

    /// <summary>
    /// Determines whether the given row is accessible to the current agent
    /// by reading the entitlement tag column and checking it against the context.
    /// </summary>
    /// <param name="payload">Raw SQLite record bytes (header + body). Do not retain.</param>
    /// <param name="rowId">The B-tree rowid of the row.</param>
    /// <returns><c>true</c> if the row's tag matches an entitlement in the context; <c>false</c> to skip.</returns>
    public bool CanAccess(ReadOnlySpan<byte> payload, long rowId)
    {
        // Parse serial types from the record header
        int colCount = _decoder.ReadSerialTypes(payload, _serialTypes, out int bodyOffset);

        // If the tag column is beyond what this record has, reject
        if (_tagColumnOrdinal >= colCount)
            return false;

        long serialType = _serialTypes[_tagColumnOrdinal];

        // NULL tag = no entitlement = reject (unless context explicitly allows null)
        if (serialType == 0)
            return false;

        // Only TEXT serial types (odd, >= 13) are valid entitlement tags
        if (serialType < 13 || serialType % 2 == 0)
            return false;

        // Compute offset for just the tag column — O(K) where K = tagColumnOrdinal
        _decoder.ComputeColumnOffsets(
            _serialTypes.AsSpan(0, _tagColumnOrdinal + 1),
            _tagColumnOrdinal + 1,
            bodyOffset,
            _columnOffsets.AsSpan(0, _tagColumnOrdinal + 1));

        // Decode the tag string directly from the payload — no row materialization
        string tag = _decoder.DecodeStringAt(payload, serialType, _columnOffsets[_tagColumnOrdinal]);

        return _context.HasEntitlement(tag);
    }
}
