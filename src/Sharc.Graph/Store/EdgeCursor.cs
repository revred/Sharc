// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Graph.Model;

namespace Sharc.Graph.Store;

internal abstract class EdgeCursorBase : IEdgeCursor
{
    protected readonly RecordDecoder Decoder;
    protected readonly int ColumnCount;
    protected long MatchKey;
    protected int? MatchKind;
    protected readonly bool MatchIsOrigin;
    
    // Ordinals
    protected readonly int ColOrigin;
    protected readonly int ColTarget;
    protected readonly int ColKind;
    protected readonly int ColData;
    protected readonly int ColCvn;
    protected readonly int ColLvn;
    protected readonly int ColSync;
    protected readonly int ColWeight;

    // Cached per row state to avoid allocations
    protected readonly long[] SerialTypes;
    protected int BodyOffset;

    protected EdgeCursorBase(RecordDecoder decoder, int columnCount, long matchKey, int? matchKind, bool matchIsOrigin,
        int colOrigin, int colTarget, int colKind, int colData, int colCvn, int colLvn, int colSync, int colWeight)
    {
        Decoder = decoder;
        ColumnCount = columnCount;
        MatchKey = matchKey;
        MatchKind = matchKind;
        MatchIsOrigin = matchIsOrigin;
        ColOrigin = colOrigin;
        ColTarget = colTarget;
        ColKind = colKind;
        ColData = colData;
        ColCvn = colCvn;
        ColLvn = colLvn;
        ColSync = colSync;
        ColWeight = colWeight;
        
        SerialTypes = ArrayPool<long>.Shared.Rent(columnCount);
    }

    public long OriginKey => GetLong(ColOrigin);
    public long TargetKey => GetLong(ColTarget);
    public int Kind => (int)GetLong(ColKind);
    public float Weight => ColWeight >= 0 ? (float)GetDouble(ColWeight) : 1.0f;

    public ReadOnlyMemory<byte> JsonDataUtf8 => ReadOnlyMemory<byte>.Empty;

    protected abstract ReadOnlySpan<byte> CurrentPayload { get; }

    private long GetLong(int ordinal)
    {
        if (ordinal < 0 || ordinal >= ColumnCount) return 0;
        return Decoder.DecodeInt64Direct(CurrentPayload, ordinal, SerialTypes, BodyOffset);
    }

    private double GetDouble(int ordinal)
    {
        if (ordinal < 0 || ordinal >= ColumnCount) return 0;
        return Decoder.DecodeDoubleDirect(CurrentPayload, ordinal, SerialTypes, BodyOffset);
    }

    public void Reset(long matchKey, int? matchKind)
    {
        MatchKey = matchKey;
        MatchKind = matchKind;
        BodyOffset = 0;
        OnReset();
    }

    public abstract bool MoveNext();
    protected abstract void OnReset();
    public virtual void Dispose()
    {
        ArrayPool<long>.Shared.Return(SerialTypes);
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
            Decoder.ReadSerialTypes(_cursor.Payload, SerialTypes, out BodyOffset);
            long key = MatchIsOrigin ? 
                Decoder.DecodeInt64Direct(_cursor.Payload, base.ColOrigin, SerialTypes, BodyOffset) :
                Decoder.DecodeInt64Direct(_cursor.Payload, base.ColTarget, SerialTypes, BodyOffset);

            if (key != MatchKey) continue;
            
            if (MatchKind.HasValue)
            {
                int kind = (int)Decoder.DecodeInt64Direct(_cursor.Payload, base.ColKind, SerialTypes, BodyOffset);
                if (kind != MatchKind.Value) continue;
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

    public IndexEdgeCursor(IBTreeReader reader, uint indexRootPage, long matchKey, int? matchKind, RecordDecoder decoder, int colCount, bool matchIsOrigin,
        int colOrigin, int colTarget, int colKind, int colData, int colCvn, int colLvn, int colSync, int colWeight, uint tableRootPage)
        : base(decoder, colCount, matchKey, matchKind, matchIsOrigin, colOrigin, colTarget, colKind, colData, colCvn, colLvn, colSync, colWeight)
    {
        _indexCursor = reader.CreateIndexCursor(indexRootPage);
        _tableCursor = reader.CreateCursor(tableRootPage);
        _indexSerials = ArrayPool<long>.Shared.Rent(16);
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
            int indexBodyOffset;
            Decoder.ReadSerialTypes(_indexCursor.Payload, _indexSerials, out indexBodyOffset);
            
            long val = Decoder.DecodeInt64Direct(_indexCursor.Payload, 0, _indexSerials, indexBodyOffset);
            if (val > MatchKey) return false;
            if (val < MatchKey) continue; 

            // Optional kind filter if it's part of the index (multi-column index)
            if (MatchKind.HasValue && Decoder.GetColumnCount(_indexCursor.Payload) >= 3)
            {
                int kind = (int)Decoder.DecodeInt64Direct(_indexCursor.Payload, 1, _indexSerials, indexBodyOffset);
                if (kind != MatchKind.Value) continue;
            }

            long rowId = Decoder.DecodeInt64Direct(_indexCursor.Payload, Decoder.GetColumnCount(_indexCursor.Payload) - 1, _indexSerials, indexBodyOffset);
            if (_tableCursor.Seek(rowId))
            {
                Decoder.ReadSerialTypes(_tableCursor.Payload, SerialTypes, out BodyOffset);
                
                // Double check kind if not in index
                if (MatchKind.HasValue && Decoder.GetColumnCount(_indexCursor.Payload) < 3)
                {
                    int kind = (int)Decoder.DecodeInt64Direct(_tableCursor.Payload, base.ColKind, SerialTypes, BodyOffset);
                    if (kind != MatchKind.Value) continue;
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
        _indexCursor.Dispose();
        _tableCursor.Dispose();
    }
}
