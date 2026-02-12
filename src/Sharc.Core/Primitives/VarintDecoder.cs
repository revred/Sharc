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

using System.Runtime.CompilerServices;

namespace Sharc.Core.Primitives;

/// <summary>
/// High-performance SQLite varint decoder operating on spans.
/// SQLite varints are 1â€“9 bytes, big-endian, MSB continuation flag.
/// </summary>
public static class VarintDecoder
{
    /// <summary>
    /// Decodes a varint from the given span.
    /// </summary>
    /// <param name="data">Input bytes.</param>
    /// <param name="value">The decoded 64-bit value.</param>
    /// <returns>Number of bytes consumed (1â€“9).</returns>
    /// <exception cref="ArgumentException">Span is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Read(ReadOnlySpan<byte> data, out long value)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Data span is empty.", nameof(data));

        // Fast path: single byte (no continuation bit)
        byte b = data[0];
        if (b < 0x80)
        {
            value = b;
            return 1;
        }

        // Bytes 1â€“8: high bit = continuation, low 7 bits = data
        long result = b & 0x7F;
        for (int i = 1; i < 8; i++)
        {
            b = data[i];
            result = (result << 7) | (b & 0x7FL);
            if (b < 0x80)
            {
                value = result;
                return i + 1;
            }
        }

        // 9th byte: all 8 bits are data
        b = data[8];
        result = (result << 8) | b;
        value = result;
        return 9;
    }

    /// <summary>
    /// Calculates the number of bytes required to encode a varint value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetEncodedLength(long value)
    {
        // Treat as unsigned for length calculation
        ulong v = (ulong)value;
        if (v <= 0x7F) return 1;
        if (v <= 0x3FFF) return 2;
        if (v <= 0x1FFFFF) return 3;
        if (v <= 0x0FFFFFFF) return 4;
        if (v <= 0x07_FFFFFFFF) return 5;
        if (v <= 0x03FF_FFFFFFFF) return 6;
        if (v <= 0x01FFFF_FFFFFFFF) return 7;
        if (v <= 0xFFFFFF_FFFFFFFF) return 8;
        return 9;
    }

    /// <summary>
    /// Writes a varint to the destination span.
    /// </summary>
    /// <returns>Number of bytes written (1â€“9).</returns>
    public static int Write(Span<byte> destination, long value)
    {
        int len = GetEncodedLength(value);
        ulong v = (ulong)value;

        if (len == 9)
        {
            // Byte 9 gets all 8 bits
            destination[8] = (byte)(v & 0xFF);
            v >>= 8;
            // Bytes 1â€“8 get 7 data bits + continuation
            for (int i = 7; i >= 0; i--)
            {
                destination[i] = (byte)((v & 0x7F) | 0x80);
                v >>= 7;
            }
            return 9;
        }

        // Last byte: no continuation bit
        destination[len - 1] = (byte)(v & 0x7F);
        v >>= 7;

        // Remaining bytes: continuation bit set
        for (int i = len - 2; i >= 0; i--)
        {
            destination[i] = (byte)((v & 0x7F) | 0x80);
            v >>= 7;
        }

        return len;
    }
}
