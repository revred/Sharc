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

using System.Text;

namespace Sharc;

/// <summary>
/// Discriminated union for filter comparison values. Avoids boxing
/// by storing primitives inline and using a tag to select the active field.
/// </summary>
internal readonly struct TypedFilterValue
{
    internal enum Tag : byte { Null, Int64, Double, Utf8, Utf8Set, Int64Set, Int64Range, DoubleRange }

    internal Tag ValueTag { get; }
    private readonly long _int64;
    private readonly double _double;
    private readonly ReadOnlyMemory<byte> _utf8;
    private readonly ReadOnlyMemory<byte>[]? _utf8Set;
    private readonly long[]? _int64Set;
    private readonly long _int64High;     // for Between (int64)
    private readonly double _doubleHigh;  // for Between (double)

    private TypedFilterValue(Tag tag, long int64 = 0, double dbl = 0,
        ReadOnlyMemory<byte> utf8 = default, ReadOnlyMemory<byte>[]? utf8Set = null,
        long[]? int64Set = null, long int64High = 0, double doubleHigh = 0)
    {
        ValueTag = tag;
        _int64 = int64;
        _double = dbl;
        _utf8 = utf8;
        _utf8Set = utf8Set;
        _int64Set = int64Set;
        _int64High = int64High;
        _doubleHigh = doubleHigh;
    }

    internal long AsInt64() => _int64;
    internal double AsDouble() => _double;
    internal ReadOnlySpan<byte> AsUtf8() => _utf8.Span;
    internal ReadOnlyMemory<byte> AsUtf8Memory() => _utf8;
    internal ReadOnlyMemory<byte>[] AsUtf8Set() => _utf8Set!;
    internal long[] AsInt64Set() => _int64Set!;
    internal long AsInt64High() => _int64High;
    internal double AsDoubleHigh() => _doubleHigh;

    internal static TypedFilterValue FromNull() => new(Tag.Null);

    internal static TypedFilterValue FromInt64(long v) => new(Tag.Int64, int64: v);

    internal static TypedFilterValue FromDouble(double v) => new(Tag.Double, dbl: v);

    internal static TypedFilterValue FromUtf8(ReadOnlyMemory<byte> v) => new(Tag.Utf8, utf8: v);

    internal static TypedFilterValue FromUtf8(string v) =>
        new(Tag.Utf8, utf8: Encoding.UTF8.GetBytes(v));

    internal static TypedFilterValue FromInt64Set(long[] values) =>
        new(Tag.Int64Set, int64Set: values);

    internal static TypedFilterValue FromUtf8Set(string[] values)
    {
        var set = new ReadOnlyMemory<byte>[values.Length];
        for (int i = 0; i < values.Length; i++)
            set[i] = Encoding.UTF8.GetBytes(values[i]);
        return new TypedFilterValue(Tag.Utf8Set, utf8Set: set);
    }

    internal static TypedFilterValue FromInt64Range(long low, long high) =>
        new(Tag.Int64Range, int64: low, int64High: high);

    internal static TypedFilterValue FromDoubleRange(double low, double high) =>
        new(Tag.DoubleRange, dbl: low, doubleHigh: high);
}
