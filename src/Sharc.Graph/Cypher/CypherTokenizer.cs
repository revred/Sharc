// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Cypher;

/// <summary>
/// Forward-only tokenizer for minimal Cypher syntax with Sharc's pipe-heavy
/// edge operators: |> (outgoing), &lt;| (incoming), &lt;|> (bidirectional).
/// Follows the SharqTokenizer pattern — ref struct, zero-allocation hot path.
/// </summary>
internal ref struct CypherTokenizer
{
    private readonly ReadOnlySpan<char> _input;
    private int _pos;

    public CypherTokenizer(ReadOnlySpan<char> input)
    {
        _input = input;
        _pos = 0;
    }

    public CypherToken Next()
    {
        SkipWhitespace();

        if (_pos >= _input.Length)
            return new CypherToken(CypherTokenKind.Eof, _pos, 0);

        char c = _input[_pos];

        // Single-char punctuation
        switch (c)
        {
            case '(': return Single(CypherTokenKind.LParen);
            case ')': return Single(CypherTokenKind.RParen);
            case '[': return Single(CypherTokenKind.LBracket);
            case ']': return Single(CypherTokenKind.RBracket);
            case ':': return Single(CypherTokenKind.Colon);
            case ',': return Single(CypherTokenKind.Comma);
            case '=': return Single(CypherTokenKind.Equals);
            case '*': return Single(CypherTokenKind.Star);
        }

        // Dot or DotDot
        if (c == '.')
        {
            if (_pos + 1 < _input.Length && _input[_pos + 1] == '.')
            {
                var tok = new CypherToken(CypherTokenKind.DotDot, _pos, 2);
                _pos += 2;
                return tok;
            }
            return Single(CypherTokenKind.Dot);
        }

        // Pipe: |> (edge)
        if (c == '|')
        {
            int start = _pos;
            _pos++;
            if (_pos < _input.Length && _input[_pos] == '>')
            {
                _pos++;
                return new CypherToken(CypherTokenKind.Edge, start, 2);
            }
            // Standalone | is unexpected
            return new CypherToken(CypherTokenKind.Error, start, 1);
        }

        // Less-than: <| (back edge) or <|> (bidi edge)
        if (c == '<')
        {
            int start = _pos;
            if (_pos + 1 < _input.Length && _input[_pos + 1] == '|')
            {
                _pos += 2;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    _pos++;
                    return new CypherToken(CypherTokenKind.BidiEdge, start, 3);
                }
                return new CypherToken(CypherTokenKind.BackEdge, start, 2);
            }
            return Single(CypherTokenKind.Error);
        }

        // Integer literal
        if (char.IsAsciiDigit(c))
            return ReadInteger();

        // Identifier or keyword
        if (char.IsAsciiLetter(c) || c == '_')
            return ReadIdentifierOrKeyword();

        // Unknown
        return Single(CypherTokenKind.Error);
    }

    private void SkipWhitespace()
    {
        while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos]))
            _pos++;
    }

    private CypherToken Single(CypherTokenKind kind)
    {
        var tok = new CypherToken(kind, _pos, 1);
        _pos++;
        return tok;
    }

    private CypherToken ReadInteger()
    {
        int start = _pos;
        long value = 0;
        while (_pos < _input.Length && char.IsAsciiDigit(_input[_pos]))
        {
            long next = value * 10 + (_input[_pos] - '0');
            if (next < value) // overflow: wrapped negative
                throw new FormatException($"Integer literal at position {start} overflows Int64.");
            value = next;
            _pos++;
        }
        return new CypherToken(CypherTokenKind.Integer, start, _pos - start, value);
    }

    private CypherToken ReadIdentifierOrKeyword()
    {
        int start = _pos;
        while (_pos < _input.Length && (char.IsAsciiLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
            _pos++;

        int len = _pos - start;
        var text = _input.Slice(start, len);

        CypherTokenKind kind = MatchKeyword(text);
        return new CypherToken(kind, start, len);
    }

    private static CypherTokenKind MatchKeyword(ReadOnlySpan<char> text)
    {
        if (text.Equals("MATCH", StringComparison.OrdinalIgnoreCase)) return CypherTokenKind.Match;
        if (text.Equals("RETURN", StringComparison.OrdinalIgnoreCase)) return CypherTokenKind.Return;
        if (text.Equals("WHERE", StringComparison.OrdinalIgnoreCase)) return CypherTokenKind.Where;
        if (text.Equals("AND", StringComparison.OrdinalIgnoreCase)) return CypherTokenKind.And;
        if (text.Equals("shortestPath", StringComparison.OrdinalIgnoreCase)) return CypherTokenKind.ShortestPath;
        return CypherTokenKind.Identifier;
    }
}
