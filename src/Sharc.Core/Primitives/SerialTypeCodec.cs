using System.Runtime.CompilerServices;

namespace Sharc.Core.Primitives;

/// <summary>
/// Interprets SQLite serial type codes to determine storage class and byte length.
/// </summary>
public static class SerialTypeCodec
{
    /// <summary>
    /// Gets the number of bytes in the content area for the given serial type.
    /// </summary>
    /// <param name="serialType">SQLite serial type code.</param>
    /// <returns>Byte length of the value. 0 for NULL and constant integers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetContentSize(long serialType)
    {
        return serialType switch
        {
            0 => 0,    // NULL
            1 => 1,    // 8-bit int
            2 => 2,    // 16-bit int
            3 => 3,    // 24-bit int
            4 => 4,    // 32-bit int
            5 => 6,    // 48-bit int
            6 => 8,    // 64-bit int
            7 => 8,    // IEEE 754 float
            8 => 0,    // Integer constant 0
            9 => 0,    // Integer constant 1
            10 or 11 => throw new ArgumentOutOfRangeException(nameof(serialType),
                serialType, "Reserved serial types 10 and 11 are not used."),
            _ => serialType >= 12
                ? (int)((serialType - 12) / 2)  // BLOB (even) or TEXT (odd) — same formula
                : throw new ArgumentOutOfRangeException(nameof(serialType),
                    serialType, "Invalid serial type.")
        };
    }

    /// <summary>
    /// Gets the storage class for the given serial type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColumnStorageClass GetStorageClass(long serialType)
    {
        if (serialType == 0) return ColumnStorageClass.Null;
        if (serialType is >= 1 and <= 6 or 8 or 9) return ColumnStorageClass.Integer;
        if (serialType == 7) return ColumnStorageClass.Float;
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
    public static bool IsInteger(long serialType) => serialType is >= 1 and <= 6 or 8 or 9;

    /// <summary>
    /// Returns true if the serial type represents a float value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFloat(long serialType) => serialType == 7;

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
}
