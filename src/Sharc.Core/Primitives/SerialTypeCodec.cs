/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
                ? (int)((serialType - 12) / 2)  // BLOB (even) or TEXT (odd) â€” same formula
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
}
