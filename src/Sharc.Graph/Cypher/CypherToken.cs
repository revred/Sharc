// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Cypher;

/// <summary>
/// A single token from the Cypher tokenizer.
/// </summary>
internal readonly struct CypherToken
{
    public CypherTokenKind Kind { get; }
    public int Start { get; }
    public int Length { get; }
    public long IntegerValue { get; }

    public CypherToken(CypherTokenKind kind, int start, int length, long integerValue = 0)
    {
        Kind = kind;
        Start = start;
        Length = length;
        IntegerValue = integerValue;
    }

    /// <summary>
    /// Extracts the token text from the original input.
    /// </summary>
    public ReadOnlySpan<char> GetText(ReadOnlySpan<char> input) => input.Slice(Start, Length);
}
