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

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using System.Text;

namespace Sharc.Graph.Model;

/// <summary>
/// Integer key for edge addressing. Wraps the 64-bit integer that edges
/// use to reference nodes. In the Maker.AI schema, this is BarID â€”
/// a 48-bit encoded ASCII identifier.
/// </summary>
public readonly record struct NodeKey(long Value)
{
    private const int BufferSize = 8;
    
    /// <summary>Decode the integer to its ASCII representation (if applicable)</summary>
    public string ToAscii()
    {
        if (Value == 0) return "0";

        Span<byte> bytes = stackalloc byte[BufferSize];
        BinaryPrimitives.WriteInt64BigEndian(bytes, Value);
        
        // Find first non-zero byte
        int start = 0;
        
        // Skip leading zeros
        while (start < BufferSize && bytes[start] == 0)
        {
            start++;
        }
        
        if (start == BufferSize) return "0";

        // BUG-05: Validate that all bytes are printable ASCII (0x20 - 0x7E)
        for (int i = start; i < BufferSize; i++)
        {
            byte b = bytes[i];
            if (b < 0x20 || b > 0x7E)
            {
                // Not printable ASCII, return numeric representation
                return ToString();
            }
        }

        return Encoding.ASCII.GetString(bytes[start..]);
    }

    /// <summary>
    /// Encodes a 6-character ASCII string into a NodeKey.
    /// </summary>
    /// <param name="ascii">The ASCII characters to encode.</param>
    /// <returns>A new NodeKey wrapping the encoded integer.</returns>
    /// <exception cref="ArgumentException">If the input contains non-ASCII characters or is too long.</exception>
    public static NodeKey FromAscii(ReadOnlySpan<char> ascii)
    {
        if (ascii.Length > BufferSize) 
            throw new ArgumentException("ASCII key too long for 64-bit integer", nameof(ascii));

        Span<byte> bytes = stackalloc byte[BufferSize];
        bytes.Clear();
        
        int offset = BufferSize - ascii.Length;
        for (int i = 0; i < ascii.Length; i++)
        {
            char c = ascii[i];
            if (c > 127) throw new ArgumentException("Non-ASCII character in key", nameof(ascii));
            bytes[offset + i] = (byte)c;
        }
            
        return new NodeKey(BinaryPrimitives.ReadInt64BigEndian(bytes));
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Implicitly converts a NodeKey to its underlying long value.
    /// </summary>
    public static implicit operator long(NodeKey k) => k.Value;

    /// <summary>
    /// Implicitly converts a long value to a NodeKey.
    /// </summary>
    public static implicit operator NodeKey(long v) => new(v);
}
