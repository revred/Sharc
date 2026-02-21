using Sharc.Core;
using Sharc.Core.Query;
using Sharc.Core.BTree;
using Xunit;

namespace Sharc.Tests.BTree;

public sealed class WithoutRowIdCursorAdapterTests
{
    [Fact]
    public void MoveNext_DelegatesToInnerCursor()
    {
        var inner = new StubIndexCursor(moveNextResults: [true, true, false]);
        var decoder = new StubRecordDecoder();
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder);

        Assert.True(adapter.MoveNext());
        Assert.True(adapter.MoveNext());
        Assert.False(adapter.MoveNext());
    }

    [Fact]
    public void Payload_DelegatesToInnerCursor()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var inner = new StubIndexCursor(moveNextResults: [true], payload: payload);
        var decoder = new StubRecordDecoder();
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder);

        adapter.MoveNext();
        Assert.Equal(payload, adapter.Payload.ToArray());
    }

    [Fact]
    public void PayloadSize_DelegatesToInnerCursor()
    {
        var payload = new byte[] { 1, 2, 3 };
        var inner = new StubIndexCursor(moveNextResults: [true], payload: payload);
        var decoder = new StubRecordDecoder();
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder);

        adapter.MoveNext();
        Assert.Equal(3, adapter.PayloadSize);
    }

    [Fact]
    public void RowId_NoIntegerPk_ReturnsSyntheticIncrementing()
    {
        var inner = new StubIndexCursor(moveNextResults: [true, true, true, false]);
        var decoder = new StubRecordDecoder();
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder, integerPkOrdinal: -1);

        adapter.MoveNext();
        Assert.Equal(1L, adapter.RowId);
        adapter.MoveNext();
        Assert.Equal(2L, adapter.RowId);
        adapter.MoveNext();
        Assert.Equal(3L, adapter.RowId);
    }

    [Fact]
    public void RowId_WithIntegerPk_ExtractsFromRecord()
    {
        var inner = new StubIndexCursor(moveNextResults: [true, true, false]);
        var decoder = new StubRecordDecoder(columnValues: [
            ColumnValue.FromInt64(4, 42L),
            ColumnValue.FromInt64(4, 99L)
        ]);
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder, integerPkOrdinal: 0);

        adapter.MoveNext();
        Assert.Equal(42L, adapter.RowId);
        adapter.MoveNext();
        Assert.Equal(99L, adapter.RowId);
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        var inner = new StubIndexCursor(moveNextResults: []);
        var decoder = new StubRecordDecoder();
        using var adapter = new WithoutRowIdCursorAdapter(inner, decoder);

        Assert.Throws<NotSupportedException>(() => adapter.Seek(1));
    }

    [Fact]
    public void Dispose_DisposesInnerCursor()
    {
        var inner = new StubIndexCursor(moveNextResults: []);
        var decoder = new StubRecordDecoder();
        var adapter = new WithoutRowIdCursorAdapter(inner, decoder);

        adapter.Dispose();
        Assert.True(inner.Disposed);
    }

    // --- Stubs ---

    private sealed class StubIndexCursor : IIndexBTreeCursor
    {
        private readonly bool[] _moveNextResults;
        private readonly byte[] _payload;
        private int _index = -1;
        public bool Disposed { get; private set; }

        public StubIndexCursor(bool[] moveNextResults, byte[]? payload = null)
        {
            _moveNextResults = moveNextResults;
            _payload = payload ?? [0x02, 0x00]; // minimal valid payload
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _moveNextResults.Length && _moveNextResults[_index];
        }

        public void Reset() => _index = -1;

        public bool SeekFirst(long firstColumnKey) => false;

        public ReadOnlySpan<byte> Payload => _payload;
        public int PayloadSize => _payload.Length;
        public bool IsStale => false;
        public void Dispose() => Disposed = true;
    }

    private sealed class StubRecordDecoder : IRecordDecoder
    {
        public bool Matches(ReadOnlySpan<byte> payload, ResolvedFilter[] filters, long rowId, int rowidAliasOrdinal) => true;
        private readonly ColumnValue[] _columnValues;
        private int _decodeIndex = -1;

        public StubRecordDecoder(ColumnValue[]? columnValues = null)
        {
            _columnValues = columnValues ?? [];
        }

        public ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex)
        {
            _decodeIndex++;
            return _decodeIndex < _columnValues.Length ? _columnValues[_decodeIndex] : ColumnValue.Null();
        }

        public ColumnValue[] DecodeRecord(ReadOnlySpan<byte> payload) => [];
        public void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination) { }
        public int GetColumnCount(ReadOnlySpan<byte> payload) => 0;
        public int ReadSerialTypes(ReadOnlySpan<byte> payload, long[] serialTypes, out int bodyOffset)
        {
            bodyOffset = 0;
            return 0;
        }

        public void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination, ReadOnlySpan<long> serialTypes, int bodyOffset)
        {
            DecodeRecord(payload, destination);
        }

        public ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset)
        {
            return DecodeColumn(payload, columnIndex);
        }

        public int ReadSerialTypes(ReadOnlySpan<byte> payload, Span<long> serialTypes, out int bodyOffset)
        {
            bodyOffset = 0;
            return 0;
        }

        public string DecodeStringDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset) => string.Empty;
        public long DecodeInt64Direct(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset) => 0;
        public double DecodeDoubleDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset) => 0;
        public ColumnValue DecodeColumnAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset) => ColumnValue.Null();
        public long DecodeInt64At(ReadOnlySpan<byte> payload, long serialType, int columnOffset) => 0;
        public double DecodeDoubleAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset) => 0;
        public string DecodeStringAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset) => string.Empty;
        public void ComputeColumnOffsets(ReadOnlySpan<long> serialTypes, int columnCount, int bodyOffset, Span<int> offsets) { }
    }
}
