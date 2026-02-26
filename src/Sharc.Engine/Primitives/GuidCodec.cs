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

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Sharc.Core.Primitives;

/// <summary>
/// Encodes and decodes GUIDs in RFC 4122 big-endian byte order for SQLite BLOB(16) storage.
/// On disk, a GUID occupies serial type 44 (BLOB of 16 bytes).
/// </summary>
public static class GuidCodec
{
    /// <summary>
    /// The SQLite serial type for a 16-byte BLOB, which is how GUIDs are stored on disk.
    /// </summary>
    public const long GuidSerialType = 44;

    /// <summary>
    /// Encodes a GUID into a 16-byte big-endian (RFC 4122) representation.
    /// </summary>
    /// <param name="value">The GUID to encode.</param>
    /// <param name="destination">A span of at least 16 bytes to write into.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode(Guid value, Span<byte> destination)
    {
        if (destination.Length < 16)
            throw new ArgumentException("Destination must be at least 16 bytes.", nameof(destination));

        value.TryWriteBytes(destination, bigEndian: true, out _);
    }

    /// <summary>
    /// Decodes a GUID from a 16-byte big-endian (RFC 4122) representation.
    /// </summary>
    /// <param name="source">A span of exactly 16 bytes containing the GUID.</param>
    /// <returns>The decoded GUID.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid Decode(ReadOnlySpan<byte> source)
    {
        return new Guid(source, bigEndian: true);
    }

    /// <summary>
    /// Decodes a GUID from a ReadOnlyMemory of 16 bytes in big-endian format.
    /// </summary>
    /// <param name="source">A memory region of exactly 16 bytes containing the GUID.</param>
    /// <returns>The decoded GUID.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid Decode(ReadOnlyMemory<byte> source)
    {
        return new Guid(source.Span, bigEndian: true);
    }

    /// <summary>
    /// Returns true if the serial type represents a 16-byte BLOB (potential GUID).
    /// Serial type 44 = BLOB of (44-12)/2 = 16 bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsGuidShaped(long serialType) => serialType == GuidSerialType;

    /// <summary>
    /// Encodes a GUID as a pair of Int64 values in big-endian byte order.
    /// The first 8 bytes of the RFC 4122 representation become Hi,
    /// the last 8 bytes become Lo.
    /// Used for merged-column storage where a GUID is stored as two INTEGER columns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (long Hi, long Lo) ToInt64Pair(Guid value)
    {
        Span<byte> buf = stackalloc byte[16];
        value.TryWriteBytes(buf, bigEndian: true, out _);
        return (BinaryPrimitives.ReadInt64BigEndian(buf),
                BinaryPrimitives.ReadInt64BigEndian(buf[8..]));
    }

    /// <summary>
    /// Decodes a GUID from a pair of Int64 values (big-endian byte interpretation).
    /// Inverse of <see cref="ToInt64Pair"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid FromInt64Pair(long hi, long lo)
    {
        Span<byte> buf = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(buf, hi);
        BinaryPrimitives.WriteInt64BigEndian(buf[8..], lo);
        return new Guid(buf, bigEndian: true);
    }
}
