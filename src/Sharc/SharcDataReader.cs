// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.IO;
using Sharc.Core.Schema;
using Sharc.Core.Query;
using Sharc.Query;

namespace Sharc;

/// <summary>
/// Forward-only reader for iterating over table rows.
/// Designed for low-allocation sequential access to SQLite records.
/// </summary>
/// <remarks>
/// Usage pattern:
/// <code>
/// using var reader = db.CreateReader("users");
/// while (reader.Read())
/// {
///     long id = reader.GetInt64(0);
///     string name = reader.GetString(1);
/// }
/// </code>
/// </remarks>
public sealed partial class SharcDataReader : IDisposable
{
    // ══════════════════════════════════════════════════════════════════════
    // FIELD LAYOUT & FILE ORGANIZATION
    // ══════════════════════════════════════════════════════════════════════
    //
    // This partial class supports 5 mutually exclusive reader modes. Fields are
    // carefully sized and grouped to minimize object layout on the x64 CLR
    // (16 B header, refs grouped first at 8 B each, then value types by
    // descending alignment: int=4, short=2, byte=1, padded to 8-byte boundary).
    //
    // ── File Organization ──────────────────────────────────────────────
    //
    //   SharcDataReader.cs            — Fields, constructors, scan/decode, accessors, lifecycle
    //   SharcDataReader.Fingerprint.cs — Row/column fingerprinting (partial class)
    //   Fnv1aHasher.cs                — 128-bit FNV-1a hasher (ref struct)
    //   Fingerprint128.cs             — 128-bit fingerprint (readonly struct)
    //   IndexSet.cs                   — Pooled open-addressing set + SetDedupMode enum
    //
    //   Composed inner classes (mode-projected, null when inactive):
    //     CompositeState — materialized/concat/dedup mode fields
    //     FilterState    — filter-specific reference fields
    //
    // ── Modes ────────────────────────────────────────────────────────────
    //
    //   1. CURSOR MODE (PointLookup, scans):
    //      _cursor + _recordDecoder + _columns + devirt cursors + lazy arrays.
    //      _composite = null, _filter = null or non-null (filtered scan).
    //      This is the hot path — PointLookup allocates only this object +
    //      BTreeCursor (~368 B total).
    //
    //   2. MATERIALIZED MODE (aggregates, subqueries):
    //      _composite != null (holds QueryValueRows/List/Enumerator).
    //      _cursor = null, _filter = null, all lazy arrays = null.
    //
    //   3. CONCAT MODE (UNION ALL):
    //      _composite != null (holds ConcatFirst/ConcatSecond).
    //      _cursor = null, _filter = null.
    //
    //   4. DEDUP MODE (UNION/INTERSECT/EXCEPT):
    //      _composite != null (holds DedupUnderlying/Seen/RightIndex).
    //      _cursor = null, _filter = null.
    //
    //   5. FILTERED CURSOR MODE: same as (1) but _filter != null.
    //      _filter holds reference fields (FilterNode, ConcreteFilterNode,
    //      Filters, FilterSerialTypes). Mutable per-row ints stay on main
    //      reader to avoid pointer indirection in the ProcessRow hot loop.
    //
    // ── Mode-Projected Composition ───────────────────────────────────────
    //
    //   Before: 29 reference fields lived directly on this class. For cursor
    //   mode (PointLookup), 13 refs from modes 2-4 were always null = 104 B
    //   of dead weight. Two inner classes now hold mode-specific state:
    //
    //   CompositeState: 9 refs + 1 int + 1 enum + 1 bool for modes 2-4.
    //     Replaces those 11 fields with a single _composite reference.
    //     Null in cursor mode → 0 B overhead on PointLookup.
    //
    //   FilterState: 4 refs for filter config (FilterNode, ConcreteFilterNode,
    //     Filters, FilterSerialTypes). Replaces those 4 fields with a single
    //     _filter reference. Null when unfiltered → 0 B on PointLookup.
    //
    // ── _scanMode + _decodedGeneration ──────────────────────────────────
    //
    //   _scanMode (byte): encodes dispatch mode + lifecycle flags.
    //     Bits 0-1: DispatchMode (Default=0, TypedCached=1, TypedMemory=2, Disposed=3)
    //     Bit 2:    IsLazy (row header parsed, body decoded on demand)
    //     Bit 3:    IsReusable (owned by PreparedQuery, Dispose resets only)
    //
    //   _decodedGeneration (int): 32-bit per-row counter for lazy decode.
    //     Incremented once per row (ProcessRow, DecodeCurrentRow, ResetForReuse).
    //     Compared per-column against _decodedGenerations[ordinal] stamps.
    //     Full 32-bit range = ~4.3 billion row cycle before wrap. Wrap requires
    //     a column to go unaccessed for 4.3B consecutive rows — unreachable in
    //     practice. Starts at 1 (constructor) so initial stamp 0 never falsely matches.
    //
    //   INVARIANT: Reusable flag survives Dispose(). When Dispose() is called
    //   on a reusable reader, it sets ScanFlags = Disposed|Reusable.
    //   ResetForReuse() preserves Reusable and replaces dispatch bits.
    //   DisposeForReal() clears all flags before calling Dispose() to release.
    //
    // ── Field Sizing (short vs int) ──────────────────────────────────────
    //
    //   Five fields are short (2 B) instead of int (4 B), saving 10 B total:
    //
    //   _rowidAliasOrdinal: column ordinal of INTEGER PRIMARY KEY alias.
    //     Range: -1 (none) to ~2000 (SQLite max columns). short is safe.
    //
    //   _columnCount: logical column count for this reader.
    //     Range: 0 to ~2000. short is safe.
    //
    //   PhysicalColumnCount: derived property — equals _columnCount when no
    //     GUID merges, otherwise read from _physicalOrdinals[_columnCount] sentinel.
    //     No longer stored as a field — saves one short (2 B).
    //
    //   _filterBodyOffset: byte offset into payload where body data starts.
    //     Set per-row by ReadSerialTypes(). Range: 0 to max payload header
    //     size (~4000 columns × 9-byte varint = ~36 KB, but typical <100 B).
    //     SQLite max page size is 65536 B, so short (max 32767) suffices for
    //     all practical payloads. Cast from int is safe.
    //
    //   _filterColCount: number of serial types read from the filter pass.
    //     Range: 0 to ~2000. short is safe.
    //
    //   _currentBodyOffset: same as _filterBodyOffset but for the main decode
    //     pass. Same range and safety analysis.
    //
    // ── Valid State Combinations ─────────────────────────────────────────
    //
    //   Cursor mode:
    //     _cursor != null, _composite == null
    //     _recordDecoder != null, _columns != null
    //     _reusableBuffer != null, _serialTypes != null (ArrayPool rentals)
    //     _filter: null (unfiltered) or non-null (filtered scan)
    //     _scanMode base: TypedCached | TypedMemory | Default (interface cursor)
    //
    //   Materialized mode:
    //     _composite != null (QueryValueRows or QueryValueList or QueryValueEnumerator set)
    //     _cursor == null, _filter == null
    //     _reusableBuffer == null, _serialTypes == null (no ArrayPool rentals)
    //     _scanMode base: Default (falls through to composite dispatch)
    //
    //   Concat mode:
    //     _composite != null (ConcatFirst + ConcatSecond set)
    //     _cursor == null, _filter == null
    //     _scanMode base: Default
    //
    //   Dedup mode:
    //     _composite != null (DedupUnderlying + DedupSeen set)
    //     _cursor == null, _filter == null
    //     _scanMode base: Default
    //
    //   Disposed:
    //     _scanMode base: Disposed (bits 0-1 = 3)
    //     _currentRow == null
    //     If Reusable: _scanMode = Disposed|Reusable, resources preserved
    //     If not Reusable: resources released, ArrayPool returned
    //
    //   CONFLICT INVARIANTS (must never occur):
    //     _composite != null && _cursor != null  — modes are exclusive
    //     _filter != null && _composite != null   — filters only apply to cursors
    //     Disposed base mode && _currentRow != null — disposed clears current row
    //     Reusable && _composite != null — reuse only applies to cursor readers
    //
    // ── Object Layout (x64, .NET 10, LayoutKind.Auto) ──────────────────
    //
    //   CLR auto-layout reorders fields for optimal packing. Declaration
    //   order does not affect physical layout. The runtime places:
    //     1. All reference fields first (8 B each, contiguous)
    //     2. Value types by descending alignment: int(4), short(2)
    //     3. Padding to 8-byte boundary
    //
    //   SharcDataReader (176 B):
    //   ├── Object header + MT pointer                  16 B
    //   ├── 18 reference fields × 8 B                 144 B
    //   │   (CLR groups all refs contiguously regardless of declaration order)
    //   │   _cursor, _btreeCachedCursor, _btreeMemoryCursor,
    //   │   _recordDecoder, _columns, _projection,
    //   │   _mergedColumns, _physicalOrdinals, _currentRow, _reusableBuffer,
    //   │   _bTreeReader, _tableIndexes, _composite, _filter,
    //   │   _serialTypes, _decodedGenerations, _columnOffsets,
    //   │   _cachedColumnNames
    //   ├── _decodedGeneration int (4 B) + 2 shorts (4 B)  8 B  ← first 8 B value slot
    //   │   └── _decodedGeneration(4) + _rowidAliasOrdinal(2) + _columnCount(2)
    //   ├── 3 shorts (6 B) + _scanMode byte (1 B) + 1 B pad  8 B  ← second 8 B value slot
    //   │   └── _filterBodyOffset(2) + _filterColCount(2) + _currentBodyOffset(2) + _scanMode(1) + pad(1)
    //   └── Total: 16 + 144 + 8 + 8 = 176 B
    //
    //   int(4) + 5×short(10) + byte(1) = 15 B → 16 B padded → 2 × 8 B slots.
    //
    // ══════════════════════════════════════════════════════════════════════

    // ── Reference fields (18 × 8 B = 144 B) ────────────────────────────
    //
    // CLR LayoutKind.Auto (default for classes) reorders fields for optimal
    // packing regardless of declaration order. We group by logical concern
    // for readability; the runtime places all refs first, then value types
    // by descending alignment (int=4, short=2, byte=1), then pads to 8 B.

    // Cursor core (set in cursor constructor, null for composite modes)
    private readonly IBTreeCursor? _cursor;
    private readonly BTreeCursor<CachedPageSource>? _btreeCachedCursor;  // Devirtualized: JIT emits direct calls (~5 ns saved/call)
    private readonly BTreeCursor<MemoryPageSource>? _btreeMemoryCursor;  // Devirtualized: same as above for memory-backed databases
    private readonly IRecordDecoder? _recordDecoder;
    private readonly IReadOnlyList<ColumnInfo>? _columns;
    private readonly int[]? _projection;
    private readonly Dictionary<int, int[]>? _mergedColumns;
    private readonly int[]? _physicalOrdinals;
    private ColumnValue[]? _currentRow;
    private ColumnValue[]? _reusableBuffer;

    // Index seek support
    private readonly IBTreeReader? _bTreeReader;
    private readonly IReadOnlyList<IndexInfo>? _tableIndexes;

    // Mode-projected composition (null in cursor mode → 0 B overhead on PointLookup)
    private readonly CompositeState? _composite;

    // Filter state (null when unfiltered → 0 B overhead on unfiltered PointLookup)
    private FilterState? _filter;

    // Lazy decode arrays (ArrayPool rentals, null for composite modes)
    private readonly long[]? _serialTypes;
    private readonly int[]? _decodedGenerations;
    private readonly int[]? _columnOffsets;

    // Cached column names — built once on first call to GetColumnNames()
    private string[]? _cachedColumnNames;

    // ── Value-type fields ───────────────────────────────────────────────
    //
    // Physical layout (CLR auto): int(4) + 5×short(10) + byte(1) = 15 B → 16 B padded.
    // Eliminating _physicalColumnCount (derived via PhysicalColumnCount property)
    // crosses the padding boundary: 17 B → 15 B → same 16 B padded → 176 B total.

    // Per-row generation counter for lazy decode (full 32-bit, ~4.3B row cycle)
    private int _decodedGeneration;

    // Scan mode flags — byte enum with 4 flag bits
    private ScanMode _scanMode;

    // shorts (2 B each) — all values ≤ ~2000 or ≤ ~32K, safe for short
    private readonly short _rowidAliasOrdinal;   // INTEGER PRIMARY KEY alias ordinal, or -1
    private readonly short _columnCount;          // Logical column count
    private short _filterBodyOffset;              // Per-row: payload body offset from filter pass
    private short _filterColCount;                // Per-row: serial type count from filter pass
    private short _currentBodyOffset;             // Per-row: payload body offset from main decode pass

    /// <summary>
    /// Scan mode flags stored in <see cref="_scanMode"/>.
    /// </summary>
    /// <remarks>
    /// <para>Bits 0–1: dispatch mode (0=Default, 1=TypedCached, 2=TypedMemory, 3=Disposed).</para>
    /// <para>Bit 2: Lazy (row header parsed, body decoded on demand via Get*).</para>
    /// <para>Bit 3: Reusable (owned by PreparedQuery, Dispose resets instead of releasing).</para>
    /// </remarks>
    [Flags]
    private enum ScanMode : byte
    {
        Default     = 0, // Composite/dedup/concat/materialized/interface — existing cascade
        TypedCached = 1, // BTreeCursor<CachedPageSource> — devirtualized via generic specialization
        TypedMemory = 2, // BTreeCursor<MemoryPageSource> — devirtualized via generic specialization
        Disposed    = 3, // Reader has been disposed — Read() throws without field check
        Lazy        = 4, // bit 2: lazy decode active
        Reusable    = 8, // bit 3: owned by PreparedQuery
    }

    private const byte DispatchMask = 0x3;  // bits 0-1: dispatch mode
    private const byte FlagsMask    = 0xF;  // bits 0-3: all 4 flag bits

    /// <summary>Dispatch mode (bits 0-1): Default, TypedCached, TypedMemory, or Disposed.</summary>
    private ScanMode DispatchMode
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ScanMode)((byte)_scanMode & DispatchMask);
    }

    /// <summary>All flag bits (bits 0-3). Set replaces flags.</summary>
    private ScanMode ScanFlags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ScanMode)((byte)_scanMode & FlagsMask);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _scanMode = value;
    }

    private bool IsLazy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_scanMode & ScanMode.Lazy) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _scanMode = value ? (_scanMode | ScanMode.Lazy) : (_scanMode & ~ScanMode.Lazy);
    }

    private bool IsReusable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_scanMode & ScanMode.Reusable) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _scanMode = value ? (_scanMode | ScanMode.Reusable) : (_scanMode & ~ScanMode.Reusable);
    }

    /// <summary>
    /// Physical column count — equals _columnCount for normal tables,
    /// or larger when merged GUID columns (__hi/__lo pairs) exist.
    /// Derived from _physicalOrdinals to avoid storing a redundant field.
    /// </summary>
    private int PhysicalColumnCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _physicalOrdinals != null ? _physicalOrdinals[_columnCount] : _columnCount;
    }

    internal readonly record struct CursorReaderConfig
    {
        public required IReadOnlyList<ColumnInfo> Columns { get; init; }
        public int[]? Projection { get; init; }
        public IBTreeReader? BTreeReader { get; init; }
        public IReadOnlyList<IndexInfo>? TableIndexes { get; init; }
        public ResolvedFilter[]? Filters { get; init; }
        public IFilterNode? FilterNode { get; init; }
    }

    internal SharcDataReader(
        IBTreeCursor cursor,
        IRecordDecoder recordDecoder,
        CursorReaderConfig config)
    {
        var columns = config.Columns;

        _cursor = cursor;
        _recordDecoder = recordDecoder;
        _columns = columns;
        _projection = config.Projection;
        _bTreeReader = config.BTreeReader;
        _tableIndexes = config.TableIndexes;
        _columnCount = (short)columns.Count;

        // Devirtualize cursor: store concrete reference for direct dispatch in Read()
        if (cursor is BTreeCursor<CachedPageSource> cc)
            _btreeCachedCursor = cc;
        else if (cursor is BTreeCursor<MemoryPageSource> mc)
            _btreeMemoryCursor = mc;

        // Detect merged columns (__hi/__lo pairs) and build ordinal mappings.
        // When merged columns exist, buffers must be sized to physical column count.
        int physicalColumnCount = columns.Count;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].IsMergedGuidColumn)
            {
                var mergedOrdinals = columns[i].MergedPhysicalOrdinals!;
                _mergedColumns ??= new Dictionary<int, int[]>();
                _mergedColumns[i] = mergedOrdinals;
                // Compute physical column count from max physical ordinal
                int maxPhys = Math.Max(mergedOrdinals[0], mergedOrdinals[1]);
                physicalColumnCount = Math.Max(physicalColumnCount, maxPhys + 1);
            }
        }

        // Build logical → physical ordinal mapping when merges exist.
        // Allocate one extra slot at [_columnCount] to store physical column count
        // so PhysicalColumnCount property can derive it without a separate field.
        if (_mergedColumns != null)
        {
            _physicalOrdinals = new int[columns.Count + 1]; // +1 for physical count sentinel
            for (int i = 0; i < columns.Count; i++)
            {
                var phys = columns[i].MergedPhysicalOrdinals;
                if (phys is { Length: 2 })
                    _physicalOrdinals[i] = phys[0]; // hi ordinal for non-guid access
                else if (phys is { Length: 1 })
                {
                    _physicalOrdinals[i] = phys[0]; // direct physical ordinal
                    physicalColumnCount = Math.Max(physicalColumnCount, phys[0] + 1);
                }
                else
                    _physicalOrdinals[i] = i; // fallback (no merge)
            }
            _physicalOrdinals[columns.Count] = physicalColumnCount; // sentinel for PhysicalColumnCount
        }

        int bufferSize = physicalColumnCount;

        // Rent a reusable buffer from ArrayPool — returned in Dispose()
        _reusableBuffer = ArrayPool<ColumnValue>.Shared.Rent(bufferSize);

        // Always use lazy decode — parse serial type headers on Read(),
        // decode individual columns only when Get*() is called. This avoids
        // string allocation for columns that are never accessed (e.g. SELECT *
        // where the caller only reads column 0).
        _serialTypes = ArrayPool<long>.Shared.Rent(bufferSize);
        _decodedGenerations = ArrayPool<int>.Shared.Rent(bufferSize);
        _columnOffsets = ArrayPool<int>.Shared.Rent(bufferSize);
        _serialTypes.AsSpan(0, bufferSize).Clear();
        _decodedGenerations.AsSpan(0, bufferSize).Clear();

        // Initialize filter state if filters are provided
        if (config.FilterNode != null || config.Filters != null)
        {
            _filter = new FilterState(config.FilterNode, config.Filters, bufferSize);
        }

        // Detect INTEGER PRIMARY KEY (rowid alias) — SQLite stores NULL in the record
        // for this column; the real value is the b-tree key (rowid).
        _rowidAliasOrdinal = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].IsPrimaryKey &&
                columns[i].DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                // Use physical ordinal for rowid alias so it matches buffer indices
                _rowidAliasOrdinal = (short)(_physicalOrdinals != null
                    ? _physicalOrdinals[i]
                    : columns[i].Ordinal);
                break;
            }
        }

        _decodedGeneration = 1;
        _scanMode = ResolveScanMode();
    }

    /// <summary>
    /// Creates an unboxed materialized reader from <see cref="QueryValue"/> rows.
    /// Typed accessors (GetInt64, GetDouble, GetString) read values without boxing.
    /// Boxing only occurs when the caller invokes <see cref="GetValue(int)"/>.
    /// </summary>
    internal SharcDataReader(QueryValue[][] rows, string[] columnNames)
    {
        _composite = new CompositeState(rows, columnNames);
        _columnCount = (short)columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Creates an unboxed materialized reader from a List of <see cref="QueryValue"/> rows.
    /// Eliminates the <c>.ToArray()</c> copy that the array constructor requires.
    /// </summary>
    internal SharcDataReader(RowSet rows, string[] columnNames)
    {
        _composite = new CompositeState(rows, columnNames);
        _columnCount = (short)columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Creates a streaming reader from an <see cref="IEnumerable{T}"/> of rows.
    /// Used for low-allocation JOIN and filtered streaming.
    /// </summary>
    internal SharcDataReader(IEnumerable<QueryValue[]> rows, string[] columnNames)
    {
        _composite = new CompositeState(rows, columnNames);
        _columnCount = (short)columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Creates a concatenating reader that streams from <paramref name="first"/> then
    /// <paramref name="second"/>. Used for zero-materialization UNION ALL.
    /// </summary>
    internal SharcDataReader(SharcDataReader first, SharcDataReader second, string[] columnNames)
    {
        _composite = new CompositeState(first, second, columnNames);
        _columnCount = (short)columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Creates a dedup streaming reader that wraps an underlying reader and
    /// filters rows using 128-bit indexes (FNV-1a of raw cursor bytes).
    /// Used for zero-alloc UNION/INTERSECT/EXCEPT set operations.
    /// </summary>
    internal SharcDataReader(SharcDataReader underlying, SetDedupMode mode,
        IndexSet? rightIndex = null)
    {
        _composite = new CompositeState(underlying, mode, rightIndex);
        _columnCount = (short)underlying.FieldCount;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Gets the number of columns in the current result set.
    /// </summary>
    public int FieldCount => _composite?.ColumnNames?.Length
        ?? _projection?.Length
        ?? _columns!.Count;

    /// <summary>
    /// Gets the rowid of the current row.
    /// </summary>
    public long RowId => _composite?.DedupUnderlying?.RowId ?? _cursor?.RowId ?? 0;

    /// <summary>
    /// Returns true if the underlying page source has been mutated since this reader
    /// was created or last refreshed. Useful for multi-agent scenarios where one agent
    /// may have committed changes that invalidate this reader's cached state.
    /// </summary>
    public bool IsStale => _cursor?.IsStale ?? false;

    /// <summary>
    /// Seeks directly to the row with the specified rowid using B-tree binary search.
    /// This is dramatically faster than sequential scan for point lookups.
    /// After a successful seek, use typed accessors (GetInt64, GetString, etc.) to read values.
    /// After Seek, you can call Read() to continue sequential iteration from the seek position.
    /// </summary>
    /// <param name="rowId">The rowid to seek to.</param>
    /// <returns>True if an exact match was found; false otherwise.</returns>
    public bool Seek(long rowId)
    {
        ObjectDisposedException.ThrowIf(DispatchMode == ScanMode.Disposed, this);

        bool found = _cursor!.Seek(rowId);

        if (found)
        {
            DecodeCurrentRow(_cursor!.Payload);
        }
        else
        {
            _currentRow = null;
            IsLazy = false;
        }

        return found;
    }

    /// <summary>
    /// Seeks to the first row matching the given index key values.
    /// Scans the specified index B-tree for matching entries, extracts the table rowid,
    /// then seeks the table cursor to that row.
    /// </summary>
    /// <param name="indexName">Name of the index to use.</param>
    /// <param name="keyValues">Key values to match against the index columns (in index column order).</param>
    /// <returns>True if a matching row was found; false otherwise.</returns>
    /// <exception cref="ArgumentException">The index was not found or the reader was not created with index support.</exception>
    public bool SeekIndex(string indexName, params object[] keyValues)
    {
        ObjectDisposedException.ThrowIf(DispatchMode == ScanMode.Disposed, this);

        if (_bTreeReader == null || _tableIndexes == null)
            throw new ArgumentException("Reader was not created with index support.");

        var indexInfo = _tableIndexes.FirstOrDefault(i =>
            i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Index '{indexName}' not found.");

        using var indexCursor = _bTreeReader.CreateIndexCursor((uint)indexInfo.RootPage);

        // Determine sort direction for the first key column to enable early exit.
        // Index entries are stored in sorted order, so once we've passed the target
        // value we can stop scanning instead of reading the entire index.
        bool firstColumnDescending = indexInfo.Columns.Count > 0 && indexInfo.Columns[0].IsDescending;

        while (indexCursor.MoveNext())
        {
            var indexRecord = _recordDecoder!.DecodeRecord(indexCursor.Payload);
            if (indexRecord.Length < 2) continue; // Need at least one key column + rowid

            // Compare key values against the first N columns of the index record
            bool match = true;
            for (int i = 0; i < keyValues.Length && i < indexRecord.Length - 1; i++)
            {
                if (!IndexKeyMatches(indexRecord[i], keyValues[i]))
                {
                    match = false;

                    // Early exit: if the first key column is past the target value,
                    // no subsequent entries can match (B-tree sorted order).
                    if (i == 0 && IndexKeyIsPastTarget(indexRecord[0], keyValues[0], firstColumnDescending))
                    {
                        _currentRow = null;
                        IsLazy = false;
                        return false;
                    }

                    break;
                }
            }

            if (!match) continue;

            // Last column in the index record is the table rowid
            long rowId = indexRecord[^1].AsInt64();
            return Seek(rowId);
        }

        _currentRow = null;
        IsLazy = false;
        return false;
    }

    /// <summary>
    /// Checks if a single index key column matches the target value.
    /// Handles type conversions (e.g. integer to long) and string comparisons.
    /// </summary>
    private static bool IndexKeyMatches(ColumnValue indexValue, object keyValue)
    {
        return keyValue switch
        {
            long l => !indexValue.IsNull && indexValue.AsInt64() == l,
            int i => !indexValue.IsNull && indexValue.AsInt64() == i,
            string s => !indexValue.IsNull && indexValue.AsString().Equals(s, StringComparison.Ordinal),
            double d => !indexValue.IsNull && indexValue.AsDouble() == d,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if the index entry's key value is past the target in sort order,
    /// meaning no further entries can match. NULLs sort first in SQLite (smallest),
    /// so a NULL index value is never "past" the target.
    /// </summary>
    private static bool IndexKeyIsPastTarget(ColumnValue indexValue, object targetValue, bool isDescending)
    {
        if (indexValue.IsNull) return false;

        int cmp = targetValue switch
        {
            long l => indexValue.AsInt64().CompareTo(l),
            int i => indexValue.AsInt64().CompareTo((long)i),
            string s => string.Compare(indexValue.AsString(), s, StringComparison.Ordinal),
            double d => indexValue.AsDouble().CompareTo(d),
            _ => 0
        };

        // For ASC: if index value > target, we've passed it
        // For DESC: if index value < target, we've passed it
        return isDescending ? cmp < 0 : cmp > 0;
    }

    /// <summary>
    /// Resolves the optimal scan mode based on cursor type.
    /// Called once from the constructor and on each <see cref="ResetForReuse"/>.
    /// All BTreeCursor paths get devirtualized dispatch; filter handling stays in ProcessRow.
    /// </summary>
    private ScanMode ResolveScanMode()
    {
        if (_btreeCachedCursor != null) return ScanMode.TypedCached;
        if (_btreeMemoryCursor != null) return ScanMode.TypedMemory;
        return ScanMode.Default;
    }

    /// <summary>
    /// Advances the reader to the next row.
    /// </summary>
    /// <returns>True if there is another row; false if the end has been reached.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        // Hot scan mode: single switch dispatch replaces 7+ branch checks per call
        // (ObjectDisposedException + 6 mode null checks). ScanMode is pre-resolved
        // in constructor/ResetForReuse; Disposed state is encoded as a case.
        return DispatchMode switch
        {
            ScanMode.TypedCached => ScanTyped(_btreeCachedCursor!),
            ScanMode.TypedMemory => ScanTyped(_btreeMemoryCursor!),
            ScanMode.Disposed => throw new ObjectDisposedException(GetType().FullName),
            _ => ReadDefault(),
        };
    }

    /// <summary>
    /// Default scan path for composite modes (dedup, concat, materialized) and
    /// non-optimized cursor types. Contains the original branch cascade.
    /// </summary>
    private bool ReadDefault()
    {
        // Composite mode: dedup, concat, materialized — dispatch through CompositeState
        if (_composite != null)
            return _composite.ReadComposite(this);

        // Devirtualized scan with filter dispatch
        if (_btreeCachedCursor != null)
            return ScanTyped(_btreeCachedCursor);
        if (_btreeMemoryCursor != null)
            return ScanTyped(_btreeMemoryCursor);
        return ScanInterface();
    }

    /// <summary>
    /// Typed scan loop: all cursor calls (MoveNext, Payload, RowId) are direct calls
    /// because <c>BTreeCursor&lt;T&gt;</c> is sealed. Eliminates ~4 interface dispatches per row.
    /// </summary>
    private bool ScanTyped<TPageSource>(BTreeCursor<TPageSource> cursor)
        where TPageSource : class, IPageSource
    {
        while (cursor.MoveNext())
        {
            if (ProcessRow(cursor.Payload, cursor.RowId))
                return true;
        }
        _currentRow = null;
        IsLazy = false;
        return false;
    }

    /// <summary>
    /// Interface-dispatch scan loop: fallback for non-BTreeCursor cursor types
    /// (IndexSeekCursor, WithoutRowIdCursorAdapter, etc.)
    /// </summary>
    private bool ScanInterface()
    {
        while (_cursor!.MoveNext())
        {
            if (ProcessRow(_cursor.Payload, _cursor.RowId))
                return true;
        }
        _currentRow = null;
        IsLazy = false;
        return false;
    }

    /// <summary>
    /// Shared per-row logic: filter evaluation and row decode.
    /// Called from both <see cref="ScanTyped{TPageSource}"/> and <see cref="ScanInterface"/>,
    /// receiving devirtualized payload/rowId from the caller.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ProcessRow(ReadOnlySpan<byte> payload, long rowId)
    {
        // ── Hot path: concrete FilterNode — write to _serialTypes directly (zero Array.Copy) ──
        if (_filter?.ConcreteFilterNode != null)
        {
            var st = _serialTypes!;
            var decoder = _recordDecoder!;
            int colCount = decoder.ReadSerialTypes(payload, st, out int bodyOffset);
            int stCount = Math.Min(colCount, st.Length);

            if (!_filter.ConcreteFilterNode.Evaluate(payload, st.AsSpan(0, stCount), bodyOffset, rowId))
                return false;

            _currentBodyOffset = (short)bodyOffset;
            int cols = Math.Min(PhysicalColumnCount, st.Length);
            decoder.ComputeColumnOffsets(st.AsSpan(0, cols), cols, bodyOffset, _columnOffsets.AsSpan(0, cols));
            _decodedGeneration++;
            IsLazy = true;
            _currentRow = _reusableBuffer;
            return true;
        }

        // ── Cold path: interface IFilterNode (non-concrete) ──
        if (_filter?.FilterNode != null)
        {
            int filterColCount = _recordDecoder!.ReadSerialTypes(payload, _filter.FilterSerialTypes!, out int filterBodyOff);
            _filterColCount = (short)filterColCount;
            _filterBodyOffset = (short)filterBodyOff;
            int stCount = Math.Min(_filterColCount, _filter.FilterSerialTypes!.Length);

            if (!_filter.FilterNode.Evaluate(payload,
                _filter.FilterSerialTypes.AsSpan(0, stCount), _filterBodyOffset, rowId))
                return false;

            DecodeCurrentRow(payload);
            return true;
        }

        // ── Legacy ResolvedFilter path ──
        if (_filter?.Filters != null && _filter.Filters.Length > 0 &&
            !_recordDecoder!.Matches(payload, _filter.Filters, rowId, _rowidAliasOrdinal))
        {
            return false;
        }

        DecodeCurrentRow(payload);
        return true;
    }

    /// <summary>
    /// Evaluates all registered filters against the currently decoded row.
    /// Ensures that lazy-decoded columns are fully available before filter execution.
    /// </summary>
    private bool EvaluateFilters()
    {
        // Filters require full column values. When in lazy mode (projection),
        // force a full decode so filter columns are available.
        if (IsLazy)
        {
            _recordDecoder!.DecodeRecord(_cursor!.Payload, _reusableBuffer!);
            IsLazy = false;
        }

        // Resolve INTEGER PRIMARY KEY alias — the record stores NULL,
        // but the real value is the b-tree rowid.
        if (_rowidAliasOrdinal >= 0 && _reusableBuffer![_rowidAliasOrdinal].IsNull)
        {
            _reusableBuffer[_rowidAliasOrdinal] =
                ColumnValue.FromInt64(4, _cursor!.RowId);
        }

        return FilterEvaluator.MatchesAll(_filter!.Filters!, _reusableBuffer!);
    }

    /// <summary>
    /// Prepares the current row for reading by parsing its header and precomputing column offsets.
    /// Does NOT decode body values immediately (Lazy Decoding).
    /// Accepts the payload span directly to avoid interface dispatch through <c>_cursor.Payload</c>.
    /// </summary>
    private void DecodeCurrentRow(ReadOnlySpan<byte> payload)
    {
        if (_filter?.FilterNode != null)
        {
            // Reuse the pre-parsed serial types from the filter step.
            // Both arrays are Rent(PhysicalColumnCount) — same capacity.
            int copyCount = Math.Min(_filterColCount, _serialTypes!.Length);
            Array.Copy(_filter.FilterSerialTypes!, _serialTypes, copyCount);
            _currentBodyOffset = _filterBodyOffset;
        }
        else
        {
            _recordDecoder!.ReadSerialTypes(payload, _serialTypes!, out int bodyOff);
            _currentBodyOffset = (short)bodyOff;
        }

        // Precompute cumulative column offsets — O(K) once per row.
        int colCount = Math.Min(PhysicalColumnCount, _serialTypes!.Length);
        _recordDecoder!.ComputeColumnOffsets(_serialTypes.AsSpan(0, colCount), colCount, _currentBodyOffset, _columnOffsets.AsSpan(0, colCount));

        _decodedGeneration++;
        IsLazy = true;
        _currentRow = _reusableBuffer;
    }

    /// <summary>
    /// Returns true if the column value is NULL.
    /// </summary>
    public bool IsNull(int ordinal)
    {
        if (_composite != null)
            return _composite.IsNull(this, ordinal);

        if (_currentRow == null)
            throw new InvalidOperationException("No current row. Call Read() first.");

        int logicalOrdinal = _projection != null ? _projection[ordinal] : ordinal;
        int actualOrdinal = _physicalOrdinals != null
            ? _physicalOrdinals[logicalOrdinal]
            : logicalOrdinal;

        // Fast path: in lazy mode, check serial type directly (no body decode needed).
        // _serialTypes is already populated by DecodeCurrentRow() — no re-parse needed.
        if (IsLazy)
        {
            // INTEGER PRIMARY KEY stores NULL in record; real value is rowid — not actually null
            if (actualOrdinal == _rowidAliasOrdinal && _serialTypes![actualOrdinal] == 0)
                return false;
            return _serialTypes![actualOrdinal] == 0;
        }

        return GetColumnValue(ordinal).IsNull;
    }

    /// <summary>
    /// Gets a column value as a 64-bit signed integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int ordinal)
    {
        if (_composite != null)
            return _composite.GetInt64(this, ordinal);

        // Fast path: decode directly from page span using precomputed O(1) offset
        if (IsLazy)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            if (_physicalOrdinals != null) actualOrdinal = _physicalOrdinals[actualOrdinal];
            if (actualOrdinal == _rowidAliasOrdinal)
                return _cursor!.RowId;
            return _recordDecoder!.DecodeInt64At(_cursor!.Payload, _serialTypes![actualOrdinal], _columnOffsets![actualOrdinal]);
        }
        return GetColumnValue(ordinal).AsInt64();
    }

    /// <summary>
    /// Gets a column value as a 32-bit signed integer.
    /// </summary>
    public int GetInt32(int ordinal)
    {
        return (int)GetInt64(ordinal);
    }

    /// <summary>
    /// Gets a column value as a double-precision float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int ordinal)
    {
        if (_composite != null)
            return _composite.GetDouble(this, ordinal);

        // Fast path: decode directly from page span using precomputed O(1) offset
        if (IsLazy)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            if (_physicalOrdinals != null) actualOrdinal = _physicalOrdinals[actualOrdinal];
            return _recordDecoder!.DecodeDoubleAt(_cursor!.Payload, _serialTypes![actualOrdinal], _columnOffsets![actualOrdinal]);
        }
        return GetColumnValue(ordinal).AsDouble();
    }

    /// <summary>
    /// Gets a column value as a UTF-8 string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int ordinal)
    {
        if (_composite != null)
            return _composite.GetString(this, ordinal);

        // Fast path: decode UTF-8 directly from page span using precomputed O(1) offset
        if (IsLazy)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            if (_physicalOrdinals != null) actualOrdinal = _physicalOrdinals[actualOrdinal];
            return _recordDecoder!.DecodeStringAt(_cursor!.Payload, _serialTypes![actualOrdinal], _columnOffsets![actualOrdinal]);
        }
        return GetColumnValue(ordinal).AsString();
    }

    /// <summary>
    /// Gets a column value as a byte array (BLOB).
    /// </summary>
    public byte[] GetBlob(int ordinal)
    {
        if (_composite != null)
            return _composite.GetBlob(this, ordinal);

        return GetColumnValue(ordinal).AsBytes().ToArray();
    }

    /// <summary>
    /// Gets a column value as a read-only span of bytes (zero-copy for BLOBs).
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// </summary>
    public ReadOnlySpan<byte> GetBlobSpan(int ordinal)
    {
        if (_composite != null)
            return _composite.GetBlobSpan(this, ordinal);

        return GetColumnValue(ordinal).AsBytes().Span;
    }

    /// <summary>
    /// Gets the column name at the specified ordinal.
    /// </summary>
    public string GetColumnName(int ordinal)
    {
        if (_composite?.ColumnNames != null)
            return _composite.ColumnNames[ordinal];
        if (_projection != null)
            return _columns![_projection[ordinal]].Name;
        return _columns![ordinal].Name;
    }

    /// <summary>
    /// Returns all column names as an array. Cached after first call.
    /// Used internally by the query pipeline to avoid repeated allocations.
    /// </summary>
    internal string[] GetColumnNames()
    {
        if (_composite?.ColumnNames != null) return _composite.ColumnNames;
        if (_cachedColumnNames != null) return _cachedColumnNames;
        int count = FieldCount;
        var names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = GetColumnName(i);
        _cachedColumnNames = names;
        return names;
    }

    /// <summary>
    /// Returns the ordinal of the column with the given name (case-insensitive).
    /// </summary>
    /// <param name="columnName">The column name to look up.</param>
    /// <returns>The zero-based column ordinal.</returns>
    /// <exception cref="ArgumentException">No column with the specified name exists.</exception>
    public int GetOrdinal(string columnName)
    {
        for (int i = 0; i < FieldCount; i++)
        {
            if (string.Equals(GetColumnName(i), columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new ArgumentException($"Column '{columnName}' not found.");
    }

    /// <summary>
    /// Gets the SQLite type affinity of the column value in the current row.
    /// </summary>
    public SharcColumnType GetColumnType(int ordinal)
    {
        if (_composite != null)
            return _composite.GetColumnType(this, ordinal);

        var val = GetColumnValue(ordinal);
        return val.StorageClass switch
        {
            ColumnStorageClass.Null => SharcColumnType.Null,
            ColumnStorageClass.Integral => SharcColumnType.Integral,
            ColumnStorageClass.Real => SharcColumnType.Real,
            ColumnStorageClass.Text => SharcColumnType.Text,
            ColumnStorageClass.Blob => SharcColumnType.Blob,
            _ => SharcColumnType.Null
        };
    }

    /// <summary>
    /// Gets the column value as a boxed object. Returns DBNull.Value for NULL.
    /// Prefer typed accessors for zero-allocation reads.
    /// </summary>
    public object GetValue(int ordinal)
    {
        if (_composite != null)
            return _composite.GetValue(this, ordinal);

        var val = GetColumnValue(ordinal);
        return val.StorageClass switch
        {
            ColumnStorageClass.Null => DBNull.Value,
            ColumnStorageClass.Integral => val.AsInt64(),
            ColumnStorageClass.Real => val.AsDouble(),
            ColumnStorageClass.Text => val.AsString(),
            ColumnStorageClass.Blob => val.AsBytes().ToArray(),
            _ => DBNull.Value
        };
    }

    private ColumnValue GetColumnValue(int ordinal)
    {
        if (_currentRow == null)
            throw new InvalidOperationException("No current row. Call Read() first.");

        int logicalOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        if (logicalOrdinal < 0 || logicalOrdinal >= _columnCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal,
                "Column ordinal is out of range.");

        // Map logical ordinal to physical ordinal when merged columns shift positions
        int actualOrdinal = _physicalOrdinals != null
            ? _physicalOrdinals[logicalOrdinal]
            : logicalOrdinal;

        // Lazy decode: decode this column on first access using precomputed O(1) offset
        if (IsLazy && _decodedGenerations![actualOrdinal] != _decodedGeneration)
        {
            _reusableBuffer![actualOrdinal] = _recordDecoder!.DecodeColumnAt(
                _cursor!.Payload, _serialTypes![actualOrdinal], _columnOffsets![actualOrdinal]);
            _decodedGenerations[actualOrdinal] = _decodedGeneration;
        }

        var value = _currentRow[actualOrdinal];

        // INTEGER PRIMARY KEY columns store NULL in the record; the real value is the rowid.
        if (actualOrdinal == _rowidAliasOrdinal && value.IsNull)
            return ColumnValue.FromInt64(1, _cursor!.RowId);

        return value;
    }

    /// <summary>
    /// Gets a column value as raw UTF-8 bytes without allocating a managed string.
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// For TEXT columns, this avoids the <see cref="System.Text.Encoding.UTF8"/> decode allocation.
    /// </summary>
    public ReadOnlySpan<byte> GetUtf8Span(int ordinal)
    {
        if (_composite != null)
            return _composite.GetUtf8Span(this, ordinal);

        return GetColumnValue(ordinal).AsBytes().Span;
    }

    /// <summary>
    /// Gets a column value as a GUID.
    /// Supports both BLOB(16) storage (serial type 44) and merged column storage
    /// (two Int64 columns named __hi/__lo combined into a single logical GUID).
    /// </summary>
    public Guid GetGuid(int ordinal)
    {
        if (_composite != null)
            return _composite.GetGuid(this, ordinal);

        // Merged column path: read two physical Int64 columns, combine into GUID.
        // Zero-alloc: DecodeInt64At reads directly from the page span using precomputed O(1) offset.
        if (_mergedColumns != null && _mergedColumns.TryGetValue(ordinal, out var phys))
        {
            long hi = _recordDecoder!.DecodeInt64At(
                _cursor!.Payload, _serialTypes![phys[0]], _columnOffsets![phys[0]]);
            long lo = _recordDecoder!.DecodeInt64At(
                _cursor!.Payload, _serialTypes[phys[1]], _columnOffsets[phys[1]]);
            return Core.Primitives.GuidCodec.FromInt64Pair(hi, lo);
        }

        // BLOB(16) path: standard GUID storage
        return Core.Primitives.GuidCodec.Decode(GetColumnValue(ordinal).AsBytes().Span);
    }

    // ─── Fingerprinting → SharcDataReader.Fingerprint.cs ──────────────

    // ── Reuse Support (PreparedQuery cursor+reader caching) ─────────

    /// <summary>
    /// Marks this reader as owned by a PreparedQuery. When reusable,
    /// Dispose resets traversal state instead of releasing resources,
    /// allowing the same cursor + ArrayPool buffers to be reused across Execute() calls.
    /// </summary>
    internal void MarkReusable() => IsReusable = true;

    /// <summary>
    /// Resets the reader for another iteration pass. Calls <see cref="IBTreeCursor.Reset"/>
    /// on the underlying cursor and clears all per-scan state. ArrayPool buffers and
    /// pre-computed metadata (projection, merged columns, rowid alias) are preserved.
    /// </summary>
    /// <param name="filterNode">The filter node for this execution (may differ for parameterized queries).</param>
    internal void ResetForReuse(IFilterNode? filterNode)
    {
        _cursor!.Reset();

        // Update filter (may change for parameterized queries)
        if (filterNode != null)
        {
            if (_filter != null)
                _filter.UpdateFilter(filterNode);
            else
                _filter = new FilterState(filterNode, null, PhysicalColumnCount);
        }
        else
        {
            _filter = null;
        }

        // Reset per-scan state — preserve Reusable flag, clear Lazy
        _currentRow = null;
        _decodedGeneration++;
        _cachedColumnNames = null;

        // Recompute scan mode — preserve generation and Reusable flag, replace dispatch bits
        ScanFlags = (ScanFlags & ScanMode.Reusable) | ResolveScanMode();
    }

    /// <summary>
    /// Performs actual resource release. Called by PreparedQuery.Dispose
    /// when the owning handle is destroyed.
    /// </summary>
    internal void DisposeForReal()
    {
        IsReusable = false;
        ScanFlags = ScanMode.Default; // Clear all flags — allow Dispose() to run
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (DispatchMode == ScanMode.Disposed) return;

        // Reusable readers: mark Disposed but preserve Reusable flag for ResetForReuse
        if (IsReusable)
        {
            ScanFlags = ScanMode.Disposed | ScanMode.Reusable;
            _currentRow = null;
            return;
        }

        ScanFlags = ScanMode.Disposed;

        _composite?.Dispose();
        _cursor?.Dispose();

        if (_reusableBuffer is not null)
        {
            ArrayPool<ColumnValue>.Shared.Return(_reusableBuffer, clearArray: true);
            _reusableBuffer = null;
        }

        if (_serialTypes is not null)
            ArrayPool<long>.Shared.Return(_serialTypes);
        if (_decodedGenerations is not null)
            ArrayPool<int>.Shared.Return(_decodedGenerations);
        if (_columnOffsets is not null)
            ArrayPool<int>.Shared.Return(_columnOffsets);

        _filter?.Dispose();
    }

    // ─── Inner State Classes ─────────────────────────────────────

    /// <summary>
    /// Holds state for non-cursor modes: materialized (QueryValue), concat (UNION ALL),
    /// and dedup (UNION/INTERSECT/EXCEPT). Null for cursor-mode readers, saving ~96 B
    /// of dead reference fields on the hot PointLookup path.
    /// </summary>
    private sealed class CompositeState : IDisposable
    {
        // Materialized mode
        internal readonly QueryValue[][]? QueryValueRows;
        internal readonly RowSet? QueryValueList;
        internal IEnumerator<QueryValue[]>? QueryValueEnumerator;
        internal int MaterializedIndex = -1;

        // Column names (shared across all composite modes)
        internal readonly string[]? ColumnNames;

        // Concat mode
        internal readonly SharcDataReader? ConcatFirst;
        internal readonly SharcDataReader? ConcatSecond;
        internal bool ConcatOnSecond;

        // Dedup mode
        internal readonly SharcDataReader? DedupUnderlying;
        internal readonly SetDedupMode DedupMode;
        internal readonly IndexSet? DedupRightIndex;
        internal readonly IndexSet? DedupSeen;

        /// <summary>Materialized from array.</summary>
        internal CompositeState(QueryValue[][] rows, string[] columnNames)
        {
            QueryValueRows = rows;
            ColumnNames = columnNames;
        }

        /// <summary>Materialized from RowSet.</summary>
        internal CompositeState(RowSet rows, string[] columnNames)
        {
            QueryValueList = rows;
            ColumnNames = columnNames;
        }

        /// <summary>Materialized from streaming enumerator.</summary>
        internal CompositeState(IEnumerable<QueryValue[]> rows, string[] columnNames)
        {
            QueryValueEnumerator = rows.GetEnumerator();
            ColumnNames = columnNames;
        }

        /// <summary>Concat mode.</summary>
        internal CompositeState(SharcDataReader first, SharcDataReader second, string[] columnNames)
        {
            ConcatFirst = first;
            ConcatSecond = second;
            ColumnNames = columnNames;
        }

        /// <summary>Dedup mode.</summary>
        internal CompositeState(SharcDataReader underlying, SetDedupMode mode, IndexSet? rightIndex)
        {
            DedupUnderlying = underlying;
            DedupMode = mode;
            DedupRightIndex = rightIndex;
            DedupSeen = IndexSet.Rent();
            ColumnNames = underlying.GetColumnNames();
        }

        internal bool IsQueryValueMode => QueryValueRows != null || QueryValueList != null || QueryValueEnumerator != null;

        internal QueryValue[] CurrentMaterializedRow
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (QueryValueRows != null) return QueryValueRows[MaterializedIndex];
                if (QueryValueList != null) return QueryValueList[MaterializedIndex];
                return QueryValueEnumerator!.Current;
            }
        }

        private bool IsConcatMode => ConcatFirst != null;

        private SharcDataReader ActiveConcatReader => ConcatOnSecond ? ConcatSecond! : ConcatFirst!;

        /// <summary>ReadDefault dispatch for composite modes.</summary>
        internal bool ReadComposite(SharcDataReader outer)
        {
            // Dedup streaming mode: filter rows by index
            if (DedupUnderlying != null)
            {
                while (DedupUnderlying.Read())
                {
                    var fp = DedupUnderlying.GetRowFingerprint();
                    bool pass = DedupMode switch
                    {
                        SetDedupMode.Union => DedupSeen!.Add(fp),
                        SetDedupMode.Intersect => DedupRightIndex!.Contains(fp) && DedupSeen!.Add(fp),
                        SetDedupMode.Except => !DedupRightIndex!.Contains(fp) && DedupSeen!.Add(fp),
                        _ => false,
                    };
                    if (pass) return true;
                }
                return false;
            }

            // Concatenating mode: stream from first reader, then second
            if (ConcatFirst != null)
            {
                if (!ConcatOnSecond)
                {
                    if (ConcatFirst.Read()) return true;
                    ConcatOnSecond = true;
                }
                return ConcatSecond!.Read();
            }

            // Unboxed materialized mode: iterate QueryValue rows (array or list)
            if (QueryValueRows != null)
            {
                MaterializedIndex++;
                return MaterializedIndex < QueryValueRows.Length;
            }
            if (QueryValueList != null)
            {
                MaterializedIndex++;
                return MaterializedIndex < QueryValueList.Count;
            }
            if (QueryValueEnumerator != null)
            {
                return QueryValueEnumerator.MoveNext();
            }

            return false;
        }

        // ── Accessor dispatch for composite modes ──

        internal bool IsNull(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.IsNull(ordinal);
            if (IsConcatMode) return ActiveConcatReader.IsNull(ordinal);
            return CurrentMaterializedRow[ordinal].IsNull;
        }

        internal long GetInt64(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetInt64(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetInt64(ordinal);
            return CurrentMaterializedRow[ordinal].AsInt64();
        }

        internal double GetDouble(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetDouble(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetDouble(ordinal);
            var qv = CurrentMaterializedRow[ordinal];
            return qv.Type == QueryValueType.Int64 ? (double)qv.AsInt64() : qv.AsDouble();
        }

        internal string GetString(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetString(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetString(ordinal);
            return CurrentMaterializedRow[ordinal].AsString();
        }

        internal byte[] GetBlob(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetBlob(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetBlob(ordinal);
            return CurrentMaterializedRow[ordinal].AsBlob();
        }

        internal ReadOnlySpan<byte> GetBlobSpan(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetBlobSpan(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetBlobSpan(ordinal);
            return CurrentMaterializedRow[ordinal].AsBlob();
        }

        internal SharcColumnType GetColumnType(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetColumnType(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetColumnType(ordinal);
            return CurrentMaterializedRow[ordinal].Type switch
            {
                QueryValueType.Null => SharcColumnType.Null,
                QueryValueType.Int64 => SharcColumnType.Integral,
                QueryValueType.Double => SharcColumnType.Real,
                QueryValueType.Text => SharcColumnType.Text,
                QueryValueType.Blob => SharcColumnType.Blob,
                _ => SharcColumnType.Null,
            };
        }

        internal object GetValue(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetValue(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetValue(ordinal);
            return CurrentMaterializedRow[ordinal].ToObject();
        }

        internal ReadOnlySpan<byte> GetUtf8Span(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetUtf8Span(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetUtf8Span(ordinal);
            return System.Text.Encoding.UTF8.GetBytes(CurrentMaterializedRow[ordinal].AsString());
        }

        internal Guid GetGuid(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetGuid(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetGuid(ordinal);
            return Core.Primitives.GuidCodec.Decode(CurrentMaterializedRow[ordinal].AsBlob());
        }

        internal Fingerprint128 GetRowFingerprint(SharcDataReader outer)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetRowFingerprint();
            if (IsConcatMode) return ActiveConcatReader.GetRowFingerprint();
            if (IsQueryValueMode) return outer.GetMaterializedRowFingerprint();
            return outer.GetCursorRowFingerprint();
        }

        internal Fingerprint128 GetColumnFingerprint(SharcDataReader outer, int ordinal)
        {
            if (DedupUnderlying != null) return DedupUnderlying.GetColumnFingerprint(ordinal);
            if (IsConcatMode) return ActiveConcatReader.GetColumnFingerprint(ordinal);

            // Materialized mode
            ref var val = ref CurrentMaterializedRow[ordinal];
            var h = new Fnv1aHasher();
            switch (val.Type)
            {
                case QueryValueType.Int64:
                    h.AddTypeTag(0, 1);
                    h.AppendLong(val.AsInt64()); break;
                case QueryValueType.Double:
                    h.AddTypeTag(0, 2);
                    h.AppendLong(BitConverter.DoubleToInt64Bits(val.AsDouble())); break;
                case QueryValueType.Text:
                    h.AddTypeTag(0, 3);
                    h.AppendString(val.AsString()); break;
                default:
                    h.AddTypeTag(0, 0);
                    h.AppendLong(0); break;
            }
            return h.Hash;
        }

        public void Dispose()
        {
            DedupUnderlying?.Dispose();
            DedupSeen?.Dispose();
            DedupRightIndex?.Dispose();
            ConcatFirst?.Dispose();
            ConcatSecond?.Dispose();

            QueryValueEnumerator?.Dispose();
            QueryValueEnumerator = null;
        }
    }

    /// <summary>
    /// Holds filter-specific reference fields. Null when no filter is active.
    /// Mutable per-row fields (_filterBodyOffset, _filterColCount) stay on the
    /// main reader to avoid pointer indirection on the hot ProcessRow path.
    /// </summary>
    private sealed class FilterState : IDisposable
    {
        internal IFilterNode? FilterNode;
        internal FilterNode? ConcreteFilterNode;
        internal readonly ResolvedFilter[]? Filters;
        internal long[]? FilterSerialTypes;

        internal FilterState(IFilterNode? filterNode, ResolvedFilter[]? filters, int bufferSize)
        {
            FilterNode = filterNode;
            ConcreteFilterNode = filterNode as FilterNode;
            Filters = filters;

            if (filterNode != null)
            {
                FilterSerialTypes = ArrayPool<long>.Shared.Rent(bufferSize);
                FilterSerialTypes.AsSpan(0, bufferSize).Clear();
            }
        }

        internal void UpdateFilter(IFilterNode filterNode)
        {
            FilterNode = filterNode;
            ConcreteFilterNode = filterNode as FilterNode;
        }

        public void Dispose()
        {
            if (FilterSerialTypes is not null)
                ArrayPool<long>.Shared.Return(FilterSerialTypes);
        }
    }
}

// SetDedupMode (enum) → IndexSet.cs
// IndexSet (sealed class) → IndexSet.cs
