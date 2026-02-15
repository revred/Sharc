// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq;

/// <summary>
/// Thrown when the Sharq parser encounters invalid syntax.
/// Includes the character position where the error occurred.
/// </summary>
public sealed class SharqParseException : Exception
{
    /// <summary>Character position (0-based) in the source where the error occurred.</summary>
    public int Position { get; }

    /// <summary>Creates a new <see cref="SharqParseException"/> with the specified message and position.</summary>
    public SharqParseException(string message, int position)
        : base($"Sharq parse error at position {position}: {message}")
    {
        Position = position;
    }
}
