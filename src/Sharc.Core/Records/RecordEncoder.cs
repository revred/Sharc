/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;

namespace Sharc.Core.Records;

/// <summary>
/// Encodes typed column values into the SQLite record format (header + body).
/// This is the write-side inverse of <see cref="RecordDecoder"/>.
/// </summary>
internal static class RecordEncoder
{
    /// <summary>
    /// Encodes an array of column values into SQLite record format.
    /// </summary>
    /// <param name="columns">The column values to encode.</param>
    /// <param name="destination">Buffer to write the record into. Must be large enough.</param>
    /// <returns>Total bytes written.</returns>
    public static int EncodeRecord(ReadOnlySpan<ColumnValue> columns, Span<byte> destination)
    {
        // Phase 1: Compute serial types and header size
        Span<long> serialTypes = columns.Length <= 64
            ? stackalloc long[columns.Length]
            : new long[columns.Length];

        int headerPayloadSize = 0; // sum of varint lengths for serial types
        for (int i = 0; i < columns.Length; i++)
        {
            serialTypes[i] = SerialTypeCodec.GetSerialType(columns[i]);
            headerPayloadSize += VarintDecoder.GetEncodedLength(serialTypes[i]);
        }

        // Header size includes the header-size varint itself.
        // We need to solve: headerSize = varintLength(headerSize) + headerPayloadSize
        int headerSizeVarintLen = VarintDecoder.GetEncodedLength(headerPayloadSize + 1);
        int headerSize = headerSizeVarintLen + headerPayloadSize;
        // Check if adding more bytes to header-size varint changes its own length
        if (VarintDecoder.GetEncodedLength(headerSize) != headerSizeVarintLen)
        {
            headerSizeVarintLen = VarintDecoder.GetEncodedLength(headerSize + 1);
            headerSize = headerSizeVarintLen + headerPayloadSize;
        }

        // Phase 2: Write header
        int pos = VarintDecoder.Write(destination, headerSize);
        for (int i = 0; i < serialTypes.Length; i++)
            pos += VarintDecoder.Write(destination[pos..], serialTypes[i]);

        // Phase 3: Write body
        for (int i = 0; i < columns.Length; i++)
            pos += WriteValue(destination[pos..], columns[i], serialTypes[i]);

        return pos;
    }

    /// <summary>
    /// Pre-computes the total encoded size of a record without writing it.
    /// </summary>
    /// <param name="columns">The column values to measure.</param>
    /// <returns>Total byte size of the encoded record.</returns>
    public static int ComputeEncodedSize(ReadOnlySpan<ColumnValue> columns)
    {
        int headerPayloadSize = 0;
        int bodySize = 0;

        for (int i = 0; i < columns.Length; i++)
        {
            long st = SerialTypeCodec.GetSerialType(columns[i]);
            headerPayloadSize += VarintDecoder.GetEncodedLength(st);
            bodySize += SerialTypeCodec.GetContentSize(st);
        }

        int headerSizeVarintLen = VarintDecoder.GetEncodedLength(headerPayloadSize + 1);
        int headerSize = headerSizeVarintLen + headerPayloadSize;
        if (VarintDecoder.GetEncodedLength(headerSize) != headerSizeVarintLen)
        {
            headerSizeVarintLen = VarintDecoder.GetEncodedLength(headerSize + 1);
            headerSize = headerSizeVarintLen + headerPayloadSize;
        }

        return headerSize + bodySize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteValue(Span<byte> destination, ColumnValue value, long serialType)
    {
        switch (serialType)
        {
            case 0: // NULL
            case 8: // constant 0
            case 9: // constant 1
                return 0;

            case 1: // 8-bit int
                destination[0] = (byte)value.AsInt64();
                return 1;

            case 2: // 16-bit int
                BinaryPrimitives.WriteInt16BigEndian(destination, (short)value.AsInt64());
                return 2;

            case 3: // 24-bit int
            {
                int v = (int)value.AsInt64();
                destination[0] = (byte)(v >> 16);
                destination[1] = (byte)(v >> 8);
                destination[2] = (byte)v;
                return 3;
            }

            case 4: // 32-bit int
                BinaryPrimitives.WriteInt32BigEndian(destination, (int)value.AsInt64());
                return 4;

            case 5: // 48-bit int
            {
                long v = value.AsInt64();
                destination[0] = (byte)(v >> 40);
                destination[1] = (byte)(v >> 32);
                destination[2] = (byte)(v >> 24);
                destination[3] = (byte)(v >> 16);
                destination[4] = (byte)(v >> 8);
                destination[5] = (byte)v;
                return 6;
            }

            case 6: // 64-bit int
                BinaryPrimitives.WriteInt64BigEndian(destination, value.AsInt64());
                return 8;

            case 7: // double
                BinaryPrimitives.WriteDoubleBigEndian(destination, value.AsDouble());
                return 8;

            default:
            {
                // Text or Blob — copy raw bytes
                var bytes = value.AsBytes().Span;
                bytes.CopyTo(destination);
                return bytes.Length;
            }
        }
    }
}
