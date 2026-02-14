/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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

    /// <summary>Gets the integer value. Only valid when StorageClass is Integer.</summary>
    public long AsInt64() => _intValue;

    /// <summary>Gets the float value. Only valid when StorageClass is Float.</summary>
    public double AsDouble() => _floatValue;

    /// <summary>Gets the raw bytes. Valid for Text and Blob storage classes.</summary>
    public ReadOnlyMemory<byte> AsBytes() => _blobValue;

    /// <summary>Gets the text value as a string. Only valid when StorageClass is Text.</summary>
    public string AsString() => System.Text.Encoding.UTF8.GetString(_blobValue.Span);

    /// <summary>Returns true if this is a NULL value.</summary>
    public bool IsNull => StorageClass == ColumnStorageClass.Null;
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
    Blob = 4
}
