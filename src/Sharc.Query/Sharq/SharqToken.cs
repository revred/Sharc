// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq;

/// <summary>
/// A single token from the Sharq tokenizer.
/// Stores offsets into the source span rather than copying strings.
/// Numeric literals are parsed inline to avoid re-parsing.
/// </summary>
internal readonly struct SharqToken
{
    /// <summary>The type of this token.</summary>
    public SharqTokenKind Kind { get; init; }

    /// <summary>Start offset in the source span.</summary>
    public int Start { get; init; }

    /// <summary>Length in the source span.</summary>
    public int Length { get; init; }

    /// <summary>Parsed value for <see cref="SharqTokenKind.Integer"/> tokens.</summary>
    public long IntegerValue { get; init; }

    /// <summary>Parsed value for <see cref="SharqTokenKind.Float"/> tokens.</summary>
    public double FloatValue { get; init; }
}
