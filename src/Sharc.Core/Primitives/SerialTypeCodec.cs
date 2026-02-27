// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Runtime.CompilerServices;

namespace Sharc.Core.Primitives;

/// <summary>
/// Interprets SQLite serial type codes to determine storage class and byte length.
/// </summary>
public static class SerialTypeCodec
{
    /// <summary>Serial type code for NULL values.</summary>
    public const long NullSerialType = 0;
    /// <summary>Serial type code for the integer constant 0 (zero-byte storage).</summary>
    public const long ZeroSerialType = 8;
    /// <summary>Serial type code for the integer constant 1 (zero-byte storage).</summary>
    public const long OneSerialType = 9;
    // Lookup table for serial types 0-9: avoids switch overhead on WASM Mono.
    // ReadOnlySpan<byte> literal compiles to PE static data — zero allocation.
    private static ReadOnlySpan<byte> FixedSizes => new byte[] { 0, 1, 2, 3, 4, 6, 8, 8, 0, 0 };

    /// <summary>
    /// Gets the number of bytes in the content area for the given serial type.
    /// </summary>
    /// <param name="serialType">SQLite serial type code.</param>
    /// <returns>Byte length of the value. 0 for NULL and constant integers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetContentSize(long serialType)
    {
        // Fast path: indexed lookup for fixed serial types 0-9
        if ((ulong)serialType <= 9)
            return FixedSizes[(int)serialType];

        // Variable-length BLOB (even >=12) or TEXT (odd >=13) — same formula.
        // Guard against overflow: the unchecked (int) cast of a huge serial type
        // would produce a negative or nonsensical content size.
        if (serialType >= 12)
        {
            long size = (serialType - 12) / 2;
            if (size > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(serialType),
                    serialType, $"Serial type implies content size {size} which exceeds int.MaxValue.");
            return (int)size;
        }

        throw new ArgumentOutOfRangeException(nameof(serialType),
            serialType, "Reserved serial types 10 and 11 are not used.");
    }

    /// <summary>
    /// Gets the storage class for the given serial type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColumnStorageClass GetStorageClass(long serialType)
    {
        if (serialType == 0) return ColumnStorageClass.Null;
        if (serialType is >= 1 and <= 6 or 8 or 9) return ColumnStorageClass.Integral;
        if (serialType == 7) return ColumnStorageClass.Real;
        if (serialType >= 12 && (serialType & 1) == 0) return ColumnStorageClass.Blob;
        if (serialType >= 13 && (serialType & 1) == 1) return ColumnStorageClass.Text;

        throw new ArgumentOutOfRangeException(nameof(serialType), serialType, "Invalid serial type.");
    }

    /// <summary>
    /// Returns true if the serial type represents a NULL value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNull(long serialType) => serialType == 0;

    /// <summary>
    /// Returns true if the serial type represents an integer value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIntegral(long serialType) => serialType is >= 1 and <= 6 or 8 or 9;

    /// <summary>
    /// Returns true if the serial type represents a float value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReal(long serialType) => serialType == 7;

    /// <summary>
    /// Returns true if the serial type represents a text value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsText(long serialType) => serialType >= 13 && (serialType & 1) == 1;

    /// <summary>
    /// Returns true if the serial type represents a blob value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBlob(long serialType) => serialType >= 12 && (serialType & 1) == 0;

    /// <summary>
    /// Returns true if the serial type represents a GUID (16-byte BLOB, serial type 44).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsGuid(long serialType) => serialType == GuidCodec.GuidSerialType;

    /// <summary>
    /// Determines the optimal SQLite serial type for a given column value.
    /// This is the write-side inverse of <see cref="GetContentSize"/> and <see cref="GetStorageClass"/>.
    /// </summary>
    /// <param name="value">The column value to encode.</param>
    /// <returns>The SQLite serial type code.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetSerialType(ColumnValue value)
    {
        switch (value.StorageClass)
        {
            case ColumnStorageClass.Null:
                return 0;

            case ColumnStorageClass.Integral:
            {
                long v = value.AsInt64();
                if (v == 0) return ZeroSerialType;
                if (v == 1) return OneSerialType;
                if (v >= -128 && v <= 127) return 1;
                if (v >= -32768 && v <= 32767) return 2;
                if (v >= -8388608 && v <= 8388607) return 3;
                if (v >= -2147483648L && v <= 2147483647L) return 4;
                if (v >= -140737488355328L && v <= 140737488355327L) return 5;
                return 6;
            }

            case ColumnStorageClass.Real:
                return 7;

            case ColumnStorageClass.Text:
                return 2L * value.AsBytes().Length + 13;

            case ColumnStorageClass.Blob:
                return 2L * value.AsBytes().Length + 12;

            case ColumnStorageClass.UniqueId:
                return GuidCodec.GuidSerialType; // 44 = BLOB of 16 bytes

            default:
                throw new ArgumentOutOfRangeException(nameof(value),
                    value.StorageClass, "Unknown storage class.");
        }
    }
}
