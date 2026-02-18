// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core.Query;

namespace Sharc.Core;

/// <summary>
/// Decodes SQLite record format into typed column values.
/// </summary>
public interface IRecordDecoder
{
    /// <summary>
    /// Decodes a record payload into a sequence of column values.
    /// </summary>
    /// <param name="payload">The raw record bytes (header + body).</param>
    /// <returns>Decoded column values.</returns>
    ColumnValue[] DecodeRecord(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Decodes a record payload into a pre-allocated column value array.
    /// Avoids per-row allocation when the caller reuses the same array.
    /// </summary>
    /// <param name="payload">The raw record bytes (header + body).</param>
    /// <param name="destination">Pre-allocated array to fill with decoded values.</param>
    void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination);

    /// <summary>
    /// Decodes a record payload using pre-parsed serial types to avoid header re-parsing.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <param name="destination">Pre-allocated array to fill.</param>
    /// <param name="serialTypes">Pre-parsed serial types.</param>
    /// <param name="bodyOffset">Byte offset where the body begins.</param>
    void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination, ReadOnlySpan<long> serialTypes, int bodyOffset);

    /// <summary>
    /// Gets the number of columns in the record without fully decoding it.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <returns>Number of columns.</returns>
    int GetColumnCount(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Decodes a single column from a record without decoding the entire record.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <param name="columnIndex">0-based column index.</param>
    /// <returns>The decoded column value.</returns>
    ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex);

    /// <summary>
    /// Decodes a single column using pre-parsed serial types to calculate offset.
    /// </summary>
    ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset);

    /// <summary>
    /// Reads only the serial types from a record header without decoding any body data.
    /// Used for lightweight null-checking and lazy decode patterns.
    /// </summary>
    /// <param name="payload">The raw record bytes (header + body).</param>
    /// <param name="serialTypes">Pre-allocated array to fill with serial type values.
    /// Only the first min(columnCount, serialTypes.Length) entries are written.</param>
    /// <param name="bodyOffset">Receives the byte offset where the record body begins.</param>
    /// <returns>The number of columns found in the record.</returns>
    int ReadSerialTypes(ReadOnlySpan<byte> payload, long[] serialTypes, out int bodyOffset);

    /// <summary>
    /// Reads only the serial types from a record header without decoding any body data.
    /// Used for lightweight null-checking and lazy decode patterns (span-optimized).
    /// </summary>
    /// <param name="payload">The raw record bytes (header + body).</param>
    /// <param name="serialTypes">Pre-allocated span to fill with serial type values.
    /// Only the first min(columnCount, serialTypes.Length) entries are written.</param>
    /// <param name="bodyOffset">Receives the byte offset where the record body begins.</param>
    /// <returns>The number of columns found in the record.</returns>
    int ReadSerialTypes(ReadOnlySpan<byte> payload, Span<long> serialTypes, out int bodyOffset);

    /// <summary>
    /// Decodes a TEXT column directly to a string without intermediate byte[] allocation.
    /// Reads UTF-8 bytes from the page span and produces a string in one allocation.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <param name="columnIndex">0-based column index.</param>
    /// <param name="serialTypes">Pre-parsed serial types.</param>
    /// <param name="bodyOffset">Byte offset where the body begins.</param>
    /// <returns>The decoded string value.</returns>
    string DecodeStringDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset);

    /// <summary>
    /// Decodes an INTEGER column directly to a long without intermediate ColumnValue construction.
    /// </summary>
    long DecodeInt64Direct(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset);

    /// <summary>
    /// Decodes a REAL column directly to a double without intermediate ColumnValue construction.
    /// </summary>
    double DecodeDoubleDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset);

    /// <summary>
    /// Decodes a single column using a precomputed byte offset (O(1) per column).
    /// Use with <see cref="ComputeColumnOffsets"/> to eliminate the O(K) per-access
    /// offset scan in <see cref="DecodeColumn(ReadOnlySpan{byte}, int, ReadOnlySpan{long}, int)"/>.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <param name="serialType">The serial type of the target column.</param>
    /// <param name="columnOffset">The precomputed byte offset within the payload.</param>
    ColumnValue DecodeColumnAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset);

    /// <summary>
    /// Decodes an INTEGER column at a precomputed offset (O(1)).
    /// </summary>
    long DecodeInt64At(ReadOnlySpan<byte> payload, long serialType, int columnOffset);

    /// <summary>
    /// Decodes a REAL column at a precomputed offset (O(1)).
    /// </summary>
    double DecodeDoubleAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset);

    /// <summary>
    /// Decodes a TEXT column at a precomputed offset (O(1)).
    /// </summary>
    string DecodeStringAt(ReadOnlySpan<byte> payload, long serialType, int columnOffset);

    /// <summary>
    /// Computes cumulative byte offsets for all columns from pre-parsed serial types.
    /// Converts O(K) per-access offset calculation into O(1) via a single precomputation pass.
    /// </summary>
    /// <param name="serialTypes">Pre-parsed serial types.</param>
    /// <param name="columnCount">Number of columns to process.</param>
    /// <param name="bodyOffset">Byte offset where the record body begins.</param>
    /// <param name="offsets">Destination array for cumulative offsets (one per column).</param>
    void ComputeColumnOffsets(ReadOnlySpan<long> serialTypes, int columnCount, int bodyOffset, Span<int> offsets);

    /// <summary>
    /// Evaluates filters directly against the raw payload without decoding the full row.
    /// </summary>
    /// <param name="payload">The raw record bytes.</param>
    /// <param name="filters">The filters to evaluate.</param>
    /// <param name="rowId">The rowid of the current record.</param>
    /// <param name="rowidAliasOrdinal">The logical ordinal of the column that aliases the rowid (INTEGER PRIMARY KEY), or -1 if none.</param>
    /// <returns>True if the record matches all filters; otherwise, false.</returns>
    bool Matches(ReadOnlySpan<byte> payload, ResolvedFilter[] filters, long rowId, int rowidAliasOrdinal = -1);
}

/// <summary>
/// Represents a decoded SQLite column value with its storage class.
/// Designed for minimal allocation: integers and floats stored inline.
/// </summary>
public readonly struct ColumnValue
{
    /// <summary>The SQLite serial type that encoded this value.</summary>
    public long SerialType { get; }

    /// <summary>The storage class of this value.</summary>
    public ColumnStorageClass StorageClass { get; }

    // Inline storage for non-heap types
    private readonly long _intValue;
    private readonly double _floatValue;
    private readonly ReadOnlyMemory<byte> _blobValue;

    private ColumnValue(long serialType, ColumnStorageClass storageClass,
        long intValue = 0, double floatValue = 0, ReadOnlyMemory<byte> blobValue = default)
    {
        SerialType = serialType;
        StorageClass = storageClass;
        _intValue = intValue;
        _floatValue = floatValue;
        _blobValue = blobValue;
    }

    /// <summary>Creates a NULL column value.</summary>
    public static ColumnValue Null() => new(0, ColumnStorageClass.Null);

    /// <summary>Creates an integer column value.</summary>
    public static ColumnValue FromInt64(long serialType, long value) =>
        new(serialType, ColumnStorageClass.Integral, intValue: value);

    /// <summary>Creates a float column value.</summary>
    public static ColumnValue FromDouble(double value) =>
        new(7, ColumnStorageClass.Real, floatValue: value);

    /// <summary>Creates a text column value.</summary>
    public static ColumnValue Text(long serialType, ReadOnlyMemory<byte> utf8Bytes) =>
        new(serialType, ColumnStorageClass.Text, blobValue: utf8Bytes);

    /// <summary>Creates a blob column value.</summary>
    public static ColumnValue Blob(long serialType, ReadOnlyMemory<byte> data) =>
        new(serialType, ColumnStorageClass.Blob, blobValue: data);

    /// <summary>Creates a GUID column value stored as a 16-byte big-endian BLOB (serial type 44).</summary>
    public static ColumnValue FromGuid(Guid value)
    {
        var bytes = new byte[16];
        Primitives.GuidCodec.Encode(value, bytes);
        return new(Primitives.GuidCodec.GuidSerialType, ColumnStorageClass.UniqueId, blobValue: bytes);
    }

    /// <summary>Gets the integer value. Only valid when StorageClass is Integer.</summary>
    public long AsInt64() => _intValue;

    /// <summary>Gets the float value. Only valid when StorageClass is Float.</summary>
    public double AsDouble() => _floatValue;

    /// <summary>Gets the raw bytes. Valid for Text and Blob storage classes.</summary>
    public ReadOnlyMemory<byte> AsBytes() => _blobValue;

    /// <summary>Gets the text value as a string. Only valid when StorageClass is Text.</summary>
    public string AsString() => System.Text.Encoding.UTF8.GetString(_blobValue.Span);

    /// <summary>Gets the GUID value. Only valid when StorageClass is Guid.</summary>
    public Guid AsGuid() => Primitives.GuidCodec.Decode(_blobValue.Span);

    /// <summary>Returns true if this is a NULL value.</summary>
    public bool IsNull => StorageClass == ColumnStorageClass.Null;

    /// <summary>
    /// Splits a GUID into two Int64 column values for merged-column storage.
    /// The hi value contains the first 8 bytes, lo contains the last 8 bytes (big-endian).
    /// </summary>
    public static (ColumnValue Hi, ColumnValue Lo) SplitGuidForMerge(Guid value)
    {
        var (hi, lo) = Primitives.GuidCodec.ToInt64Pair(value);
        return (FromInt64(6, hi), FromInt64(6, lo));
    }
}

/// <summary>
/// SQLite storage classes.
/// </summary>
public enum ColumnStorageClass
{
    /// <summary>NULL value.</summary>
    Null = 0,
    /// <summary>Signed integer (1, 2, 3, 4, 6, or 8 bytes).</summary>
    Integral = 1,
    /// <summary>IEEE 754 64-bit float.</summary>
    Real = 2,
    /// <summary>UTF-8 text string.</summary>
    Text = 3,
    /// <summary>Binary large object.</summary>
    Blob = 4,
    /// <summary>GUID / UUID stored as 16-byte big-endian BLOB (serial type 44).</summary>
    UniqueId = 5
}