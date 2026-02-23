// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc;

/// <summary>
/// 128-bit fingerprint with packed metadata. Every bit serves a purpose:
/// <list type="bullet">
///   <item>Lo [64 bits] — Primary FNV-1a hash (bucket selection + primary comparison)</item>
///   <item>Guard [32 bits] — Secondary FNV-1a hash (96-bit total collision resistance)</item>
///   <item>PayloadLen [16 bits] — Total payload byte length (fast structural rejection)</item>
///   <item>TypeTag [16 bits] — Column type signature (structural fingerprint)</item>
/// </list>
/// Collision probability at 6M rows: P ≈ N²/2⁹⁷ ≈ 10⁻¹⁶. Practically collision-free.
/// PayloadLen and TypeTag provide instant rejection for structurally different rows
/// before the hash comparison is even reached.
/// </summary>
internal readonly struct Fingerprint128 : IEquatable<Fingerprint128>
{
    public readonly ulong Lo;
    public readonly ulong Hi;

    /// <summary>Construct from raw lo/hi (used by Fnv1aHasher).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fingerprint128(ulong lo, uint guard, ushort payloadLen, ushort typeTag)
    {
        Lo = lo;
        Hi = ((ulong)guard << 32) | ((ulong)payloadLen << 16) | typeTag;
    }

    /// <summary>32-bit secondary hash guard for collision resistance.</summary>
    public uint Guard => (uint)(Hi >> 32);
    /// <summary>Total payload byte length — instant structural rejection.</summary>
    public ushort PayloadLen => (ushort)(Hi >> 16);
    /// <summary>Column type signature — structural fingerprint.</summary>
    public ushort TypeTag => (ushort)Hi;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Fingerprint128 other) => Lo == other.Lo && Hi == other.Hi;
    public override bool Equals(object? obj) => obj is Fingerprint128 f && Equals(f);
    public override int GetHashCode() => HashCode.Combine(Lo, Hi);
}
