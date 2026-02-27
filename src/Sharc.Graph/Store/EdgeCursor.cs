// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
using System.Runtime.InteropServices;
using Sharc.Core;
using Sharc.Core.Records;

namespace Sharc.Graph.Store;

/// <summary>
/// Base class for edge cursors that iterate relation records matching a given node and direction.
/// </summary>
internal abstract class EdgeCursorBase : IEdgeCursor
{
    protected readonly RecordDecoder Decoder;
    protected readonly int ColumnCount;
    protected long MatchKey;
    protected int MatchKind;              // -1 = no filter, >= 0 = filter by kind
    protected const int NoKindFilter = -1;
    protected readonly int MatchOrdinal;  // precomputed: ColOrigin if origin, ColTarget if target

    // Column ordinals packed into a single struct
    protected readonly ColumnOrdinals Cols;

    // Cached per row state to avoid allocations
    protected readonly long[] SerialTypes;
    protected readonly int[] Offsets;
    protected int BodyOffset;

    [StructLayout(LayoutKind.Sequential)]
    protected readonly struct ColumnOrdinals
    {
        public readonly int Origin, Target, Kind, Data, Cvn, Lvn, Sync, Weight;
        public ColumnOrdinals(int origin, int target, int kind, int data,
            int cvn, int lvn, int sync, int weight)
        {
            Origin = origin; Target = target; Kind = kind; Data = data;
            Cvn = cvn; Lvn = lvn; Sync = sync; Weight = weight;
        }
    }

    /// <summary>Maximum supported column count for ArrayPool rentals to prevent excessive memory allocation.</summary>
    private const int MaxColumnCount = 1024;

    protected EdgeCursorBase(RecordDecoder decoder, int columnCount, long matchKey, int? matchKind, bool matchIsOrigin,
        int colOrigin, int colTarget, int colKind, int colData, int colCvn, int colLvn, int colSync, int colWeight)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columnCount, MaxColumnCount);

        Decoder = decoder;
        ColumnCount = columnCount;
        MatchKey = matchKey;
        MatchKind = matchKind ?? NoKindFilter;
        MatchOrdinal = matchIsOrigin ? colOrigin : colTarget;
        Cols = new ColumnOrdinals(colOrigin, colTarget, colKind, colData, colCvn, colLvn, colSync, colWeight);

        SerialTypes = ArrayPool<long>.Shared.Rent(columnCount);
        Offsets = ArrayPool<int>.Shared.Rent(columnCount);
    }

    /// <summary>
    /// Precomputes column byte offsets after ReadSerialTypes.
    /// Converts O(K) per-column DecodeXxxDirect to O(1) DecodeXxxAt.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    protected void PrepareOffsets()
    {
        Decoder.ComputeColumnOffsets(SerialTypes, ColumnCount, BodyOffset, Offsets);
    }

    public long OriginKey => GetLong(Cols.Origin);
    public long TargetKey => GetLong(Cols.Target);
    public int Kind => (int)GetLong(Cols.Kind);
    public float Weight => Cols.Weight >= 0 ? (float)GetDouble(Cols.Weight) : 1.0f;

    public ReadOnlyMemory<byte> JsonDataUtf8 => ReadOnlyMemory<byte>.Empty;

    protected abstract ReadOnlySpan<byte> CurrentPayload { get; }

    private long GetLong(int ordinal)
    {
        if (ordinal < 0 || ordinal >= ColumnCount) return 0;
        return Decoder.DecodeInt64At(CurrentPayload, SerialTypes[ordinal], Offsets[ordinal]);
    }

    private double GetDouble(int ordinal)
    {
        if (ordinal < 0 || ordinal >= ColumnCount) return 0;
        return Decoder.DecodeDoubleAt(CurrentPayload, SerialTypes[ordinal], Offsets[ordinal]);
    }

    public void Reset(long matchKey, int? matchKind)
    {
        MatchKey = matchKey;
        MatchKind = matchKind ?? NoKindFilter;
        BodyOffset = 0;
        OnReset();
    }

    public abstract bool MoveNext();
    protected abstract void OnReset();
    public virtual void Dispose()
    {
        ArrayPool<long>.Shared.Return(SerialTypes);
        ArrayPool<int>.Shared.Return(Offsets);
    }
}

internal sealed class TableScanEdgeCursor : EdgeCursorBase
{
    private readonly IBTreeCursor _cursor;

    public TableScanEdgeCursor(IBTreeReader reader, uint rootPage, long matchKey, int? matchKind, bool matchIsOrigin,
        RecordDecoder decoder, int colCount, int colOrigin, int colTarget, int colKind, int colData,
        int colCvn, int colLvn, int colSync, int colWeight)
        : base(decoder, colCount, matchKey, matchKind, matchIsOrigin, colOrigin, colTarget, colKind, colData, colCvn, colLvn, colSync, colWeight)
    {
        _cursor = reader.CreateCursor(rootPage);
    }

    protected override void OnReset() => _cursor.Reset();
    protected override ReadOnlySpan<byte> CurrentPayload => _cursor.Payload;

    public override bool MoveNext()
    {
        while (_cursor.MoveNext())
        {
            var payload = _cursor.Payload;
            Decoder.ReadSerialTypes(payload, SerialTypes, out BodyOffset);
            PrepareOffsets();

            long key = Decoder.DecodeInt64At(payload, SerialTypes[MatchOrdinal], Offsets[MatchOrdinal]);

            if (key != MatchKey) continue;

            if (MatchKind >= 0)
            {
                int kind = (int)Decoder.DecodeInt64At(payload, SerialTypes[Cols.Kind], Offsets[Cols.Kind]);
                if (kind != MatchKind) continue;
            }

            return true;
        }
        return false;
    }

    public override void Dispose()
    {
        base.Dispose();
        _cursor.Dispose();
    }
}

internal sealed class IndexEdgeCursor : EdgeCursorBase
{
    private readonly IIndexBTreeCursor _indexCursor;
    private readonly IBTreeCursor _tableCursor;
    private readonly long[] _indexSerials;
    private readonly int[] _indexOffsets;

    public IndexEdgeCursor(IBTreeReader reader, uint indexRootPage, long matchKey, int? matchKind, RecordDecoder decoder, int colCount, bool matchIsOrigin,
        int colOrigin, int colTarget, int colKind, int colData, int colCvn, int colLvn, int colSync, int colWeight, uint tableRootPage)
        : base(decoder, colCount, matchKey, matchKind, matchIsOrigin, colOrigin, colTarget, colKind, colData, colCvn, colLvn, colSync, colWeight)
    {
        _indexCursor = reader.CreateIndexCursor(indexRootPage);
        _tableCursor = reader.CreateCursor(tableRootPage);
        _indexSerials = ArrayPool<long>.Shared.Rent(16);
        _indexOffsets = ArrayPool<int>.Shared.Rent(16);
    }

    protected override ReadOnlySpan<byte> CurrentPayload => _tableCursor.Payload;

    protected override void OnReset()
    {
        _indexCursor.Reset();
        _tableCursor.Reset();
    }

    public override bool MoveNext()
    {
        if (_indexCursor.PayloadSize == 0)
        {
            if (!_indexCursor.SeekFirst(MatchKey)) return false;
        }
        else
        {
            if (!_indexCursor.MoveNext()) return false;
        }

        do
        {
            var indexPayload = _indexCursor.Payload;
            int indexColCount = Decoder.ReadSerialTypes(indexPayload, _indexSerials, out int indexBodyOffset);
            Decoder.ComputeColumnOffsets(_indexSerials, indexColCount, indexBodyOffset, _indexOffsets);

            long val = Decoder.DecodeInt64At(indexPayload, _indexSerials[0], _indexOffsets[0]);
            if (val > MatchKey) return false;
            if (val < MatchKey) continue;

            // Optional kind filter if it's part of the index (multi-column index)
            if (MatchKind >= 0 && indexColCount >= 3)
            {
                int kind = (int)Decoder.DecodeInt64At(indexPayload, _indexSerials[1], _indexOffsets[1]);
                if (kind != MatchKind) continue;
            }

            long rowId = Decoder.DecodeInt64At(indexPayload, _indexSerials[indexColCount - 1], _indexOffsets[indexColCount - 1]);
            if (_tableCursor.Seek(rowId))
            {
                var tablePayload = _tableCursor.Payload;
                Decoder.ReadSerialTypes(tablePayload, SerialTypes, out BodyOffset);
                PrepareOffsets();

                // Double check kind if not in index
                if (MatchKind >= 0 && indexColCount < 3)
                {
                    int kind = (int)Decoder.DecodeInt64At(tablePayload, SerialTypes[Cols.Kind], Offsets[Cols.Kind]);
                    if (kind != MatchKind) continue;
                }

                return true;
            }
        }
        while (_indexCursor.MoveNext());

        return false;
    }

    public override void Dispose()
    {
        base.Dispose();
        ArrayPool<long>.Shared.Return(_indexSerials);
        ArrayPool<int>.Shared.Return(_indexOffsets);
        _indexCursor.Dispose();
        _tableCursor.Dispose();
    }
}
