// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Sharc.Query.Sharq;

/// <summary>
/// Zero-allocation tokenizer for Sharq queries.
/// Operates directly on a <see cref="ReadOnlySpan{T}"/> of characters.
/// Produces tokens on demand — no intermediate token list.
/// </summary>
internal ref struct SharqTokenizer
{
    // SIMD-accelerated character class scanning (16-32 chars per CPU cycle)
    private static readonly SearchValues<char> s_identChars = SearchValues.Create(
        "_0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    private static readonly SearchValues<char> s_digits = SearchValues.Create("0123456789");

    // Zero-alloc keyword lookup via FrozenDictionary alternate span key
    private static readonly FrozenDictionary<string, SharqTokenKind> s_keywords =
        new Dictionary<string, SharqTokenKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["CREATE"] = SharqTokenKind.Create,
            ["VIEW"] = SharqTokenKind.View,
            ["SELECT"] = SharqTokenKind.Select,
            ["FROM"] = SharqTokenKind.From,
            ["WHERE"] = SharqTokenKind.Where,
            ["GROUP"] = SharqTokenKind.Group,
            ["BY"] = SharqTokenKind.By,
            ["ORDER"] = SharqTokenKind.Order,
            ["ASC"] = SharqTokenKind.Asc,
            ["DESC"] = SharqTokenKind.Desc,
            ["LIMIT"] = SharqTokenKind.Limit,
            ["OFFSET"] = SharqTokenKind.Offset,
            ["AND"] = SharqTokenKind.And,
            ["OR"] = SharqTokenKind.Or,
            ["NOT"] = SharqTokenKind.Not,
            ["IN"] = SharqTokenKind.In,
            ["BETWEEN"] = SharqTokenKind.Between,
            ["LIKE"] = SharqTokenKind.Like,
            ["IS"] = SharqTokenKind.Is,
            ["NULL"] = SharqTokenKind.Null,
            ["TRUE"] = SharqTokenKind.True,
            ["FALSE"] = SharqTokenKind.False,
            ["AS"] = SharqTokenKind.As,
            ["DISTINCT"] = SharqTokenKind.Distinct,
            ["CASE"] = SharqTokenKind.Case,
            ["WHEN"] = SharqTokenKind.When,
            ["THEN"] = SharqTokenKind.Then,
            ["ELSE"] = SharqTokenKind.Else,
            ["END"] = SharqTokenKind.End,
            ["HAVING"] = SharqTokenKind.Having,
            ["CAST"] = SharqTokenKind.Cast,
            ["WITH"] = SharqTokenKind.With,
            ["OVER"] = SharqTokenKind.Over,
            ["PARTITION"] = SharqTokenKind.Partition,
            ["NULLS"] = SharqTokenKind.Nulls,
            ["UNION"] = SharqTokenKind.Union,
            ["INTERSECT"] = SharqTokenKind.Intersect,
            ["EXCEPT"] = SharqTokenKind.Except,
            ["EXISTS"] = SharqTokenKind.Exists,
            ["JOIN"] = SharqTokenKind.Join,
            ["INNER"] = SharqTokenKind.Inner,
            ["LEFT"] = SharqTokenKind.Left,
            ["RIGHT"] = SharqTokenKind.Right,
            ["CROSS"] = SharqTokenKind.Cross,
            ["ON"] = SharqTokenKind.On,
            ["DIRECT"] = SharqTokenKind.Direct,
            ["CACHED"] = SharqTokenKind.Cached,
            ["JIT"] = SharqTokenKind.Jit,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ReadOnlySpan<char> _sql;
    private int _pos;
    private SharqToken _peeked;
    private bool _hasPeeked;

    public SharqTokenizer(ReadOnlySpan<char> sql)
    {
        _sql = sql;
        _pos = 0;
        _peeked = default;
        _hasPeeked = false;
    }

    /// <summary>
    /// Returns the next token, advancing the position.
    /// </summary>
    public SharqToken NextToken()
    {
        if (_hasPeeked)
        {
            _hasPeeked = false;
            return _peeked;
        }
        return ScanToken();
    }

    /// <summary>
    /// Returns the next token without advancing the position.
    /// </summary>
    public SharqToken Peek()
    {
        if (!_hasPeeked)
        {
            _peeked = ScanToken();
            _hasPeeked = true;
        }
        return _peeked;
    }

    /// <summary>
    /// Returns the text of a token by slicing the source span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<char> GetText(SharqToken token)
        => _sql.Slice(token.Start, token.Length);

    private SharqToken ScanToken()
    {
        SkipWhitespace();

        if (_pos >= _sql.Length)
            return new SharqToken { Kind = SharqTokenKind.Eof, Start = _pos, Length = 0 };

        char c = _sql[_pos];

        // Single-character tokens
        switch (c)
        {
            case ',': return Simple(SharqTokenKind.Comma);
            case '(': return Simple(SharqTokenKind.LeftParen);
            case ')': return Simple(SharqTokenKind.RightParen);
            case ';': return Simple(SharqTokenKind.Semicolon);
            case '+': return Simple(SharqTokenKind.Plus);
            case '/': return Simple(SharqTokenKind.Slash);
            case '%': return Simple(SharqTokenKind.Percent);
            case ':': return Simple(SharqTokenKind.Colon);
        }

        // Dot or float starting with dot
        if (c == '.')
        {
            if (_pos + 1 < _sql.Length && char.IsAsciiDigit(_sql[_pos + 1]))
                return ScanNumber();
            return Simple(SharqTokenKind.Dot);
        }

        // Star (must come after dot check since we don't want .* to become Dot + Star prematurely)
        if (c == '*')
            return Simple(SharqTokenKind.Star);

        // Minus (just minus, no arrow)
        if (c == '-')
            return Simple(SharqTokenKind.Minus);

        // Pipe: |> (edge), |u (union), |a (union all), |n (intersect), |x (except), |? (exists)
        if (c == '|')
        {
            int start = _pos;
            _pos++;
            if (_pos < _sql.Length)
            {
                switch (_sql[_pos])
                {
                    case '>': _pos++; return new SharqToken { Kind = SharqTokenKind.Edge, Start = start, Length = 2 };
                    case 'u': _pos++; return new SharqToken { Kind = SharqTokenKind.Union, Start = start, Length = 2 };
                    case 'a': _pos++; return new SharqToken { Kind = SharqTokenKind.UnionAll, Start = start, Length = 2 };
                    case 'n': _pos++; return new SharqToken { Kind = SharqTokenKind.Intersect, Start = start, Length = 2 };
                    case 'x': _pos++; return new SharqToken { Kind = SharqTokenKind.Except, Start = start, Length = 2 };
                    case '?': _pos++; return new SharqToken { Kind = SharqTokenKind.Exists, Start = start, Length = 2 };
                }
            }
            // Standalone | is unexpected; return EOF-like
            return new SharqToken { Kind = SharqTokenKind.Eof, Start = start, Length = 1 };
        }

        // Less-than: could be <, <=, <>, <|, <|>
        if (c == '<')
        {
            int start = _pos;
            _pos++;
            if (_pos < _sql.Length)
            {
                if (_sql[_pos] == '=')
                {
                    _pos++;
                    return new SharqToken { Kind = SharqTokenKind.LessOrEqual, Start = start, Length = 2 };
                }
                if (_sql[_pos] == '>')
                {
                    _pos++;
                    return new SharqToken { Kind = SharqTokenKind.NotEqual, Start = start, Length = 2 };
                }
                if (_sql[_pos] == '|')
                {
                    _pos++;
                    if (_pos < _sql.Length && _sql[_pos] == '>')
                    {
                        _pos++;
                        return new SharqToken { Kind = SharqTokenKind.BidiEdge, Start = start, Length = 3 };
                    }
                    return new SharqToken { Kind = SharqTokenKind.BackEdge, Start = start, Length = 2 };
                }
            }
            return new SharqToken { Kind = SharqTokenKind.LessThan, Start = start, Length = 1 };
        }

        // Greater-than: could be > or >=
        if (c == '>')
        {
            int start = _pos;
            _pos++;
            if (_pos < _sql.Length && _sql[_pos] == '=')
            {
                _pos++;
                return new SharqToken { Kind = SharqTokenKind.GreaterOrEqual, Start = start, Length = 2 };
            }
            return new SharqToken { Kind = SharqTokenKind.GreaterThan, Start = start, Length = 1 };
        }

        // Equal
        if (c == '=')
            return Simple(SharqTokenKind.Equal);

        // Not-equal: !=
        if (c == '!')
        {
            int start = _pos;
            _pos++;
            if (_pos < _sql.Length && _sql[_pos] == '=')
            {
                _pos++;
                return new SharqToken { Kind = SharqTokenKind.NotEqual, Start = start, Length = 2 };
            }
            // Standalone '!' is unexpected; return it and let parser handle the error
            return new SharqToken { Kind = SharqTokenKind.Eof, Start = start, Length = 1 };
        }

        // @ operators: @@, @AND@, @OR@
        if (c == '@')
            return ScanAtOperator();

        // String literal
        if (c == '\'')
            return ScanString();

        // Quoted identifiers
        if (c == '"' || c == '[' || c == '`')
            return ScanQuotedIdentifier();

        // Number
        if (char.IsAsciiDigit(c))
            return ScanNumber();

        // Parameter reference: $name
        if (c == '$')
            return ScanParameter();

        // Identifier or keyword
        if (char.IsLetter(c) || c == '_')
            return ScanIdentifierOrKeyword();

        // Unknown character — skip and return EOF-like (parser will handle)
        _pos++;
        return new SharqToken { Kind = SharqTokenKind.Eof, Start = _pos - 1, Length = 1 };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SharqToken Simple(SharqTokenKind kind)
    {
        int start = _pos;
        _pos++;
        return new SharqToken { Kind = kind, Start = start, Length = 1 };
    }

    private void SkipWhitespace()
    {
        while (_pos < _sql.Length)
        {
            if (char.IsWhiteSpace(_sql[_pos]))
            {
                _pos++;
                continue;
            }

            // -- line comment
            if (_pos + 1 < _sql.Length && _sql[_pos] == '-' && _sql[_pos + 1] == '-')
            {
                _pos += 2;
                while (_pos < _sql.Length && _sql[_pos] != '\n')
                    _pos++;
                continue;
            }

            // /* block comment */
            if (_pos + 1 < _sql.Length && _sql[_pos] == '/' && _sql[_pos + 1] == '*')
            {
                _pos += 2;
                while (_pos < _sql.Length)
                {
                    if (_sql[_pos] == '*' && _pos + 1 < _sql.Length && _sql[_pos + 1] == '/')
                    {
                        _pos += 2;
                        break;
                    }
                    _pos++;
                }
                continue;
            }

            break;
        }
    }

    private SharqToken ScanString()
    {
        int start = _pos; // position of opening quote
        _pos++; // skip opening '
        int contentStart = _pos;

        while (_pos < _sql.Length)
        {
            if (_sql[_pos] == '\'')
            {
                // Check for escaped quote ''
                if (_pos + 1 < _sql.Length && _sql[_pos + 1] == '\'')
                {
                    _pos += 2; // skip ''
                    continue;
                }
                int contentLength = _pos - contentStart;
                _pos++; // skip closing '
                return new SharqToken { Kind = SharqTokenKind.String, Start = contentStart, Length = contentLength };
            }
            _pos++;
        }

        // Unterminated string — return what we have (parser will report error)
        return new SharqToken { Kind = SharqTokenKind.String, Start = contentStart, Length = _pos - contentStart };
    }

    private SharqToken ScanQuotedIdentifier()
    {
        char openChar = _sql[_pos];
        char closeChar = openChar == '[' ? ']' : openChar;
        _pos++; // skip opening quote
        int contentStart = _pos;

        while (_pos < _sql.Length && _sql[_pos] != closeChar)
            _pos++;

        int contentLength = _pos - contentStart;
        if (_pos < _sql.Length) _pos++; // skip closing quote

        return new SharqToken { Kind = SharqTokenKind.Identifier, Start = contentStart, Length = contentLength };
    }

    private SharqToken ScanNumber()
    {
        int start = _pos;
        bool hasDot = false;

        // Handle leading dot (e.g., .5)
        if (_pos < _sql.Length && _sql[_pos] == '.')
        {
            hasDot = true;
            _pos++;
        }

        SkipDigits();

        if (!hasDot && _pos < _sql.Length && _sql[_pos] == '.')
        {
            // Check next char to distinguish "42.name" (dot access) from "42.5" (float)
            if (_pos + 1 < _sql.Length && char.IsAsciiDigit(_sql[_pos + 1]))
            {
                hasDot = true;
                _pos++; // consume dot
                SkipDigits();
            }
        }

        // Scientific notation (e.g., 1e10, 1.5e-3)
        if (_pos < _sql.Length && (_sql[_pos] == 'e' || _sql[_pos] == 'E'))
        {
            hasDot = true; // treat as float
            _pos++;
            if (_pos < _sql.Length && (_sql[_pos] == '+' || _sql[_pos] == '-'))
                _pos++;
            SkipDigits();
        }

        int length = _pos - start;
        var numSpan = _sql.Slice(start, length);

        if (hasDot)
        {
            _ = double.TryParse(numSpan, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double dval);
            return new SharqToken { Kind = SharqTokenKind.Float, Start = start, Length = length, FloatValue = dval };
        }
        else
        {
            _ = long.TryParse(numSpan, out long ival);
            return new SharqToken { Kind = SharqTokenKind.Integer, Start = start, Length = length, IntegerValue = ival };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipDigits()
    {
        var remaining = _sql[_pos..];
        int end = remaining.IndexOfAnyExcept(s_digits);
        _pos += end < 0 ? remaining.Length : end;
    }

    private SharqToken ScanIdentifierOrKeyword()
    {
        int start = _pos;

        // SIMD: scan identifier chars 16-32 at a time
        var remaining = _sql[_pos..];
        int end = remaining.IndexOfAnyExcept(s_identChars);
        _pos += end < 0 ? remaining.Length : end;

        int length = _pos - start;
        var word = _sql.Slice(start, length);

        var lookup = s_keywords.GetAlternateLookup<ReadOnlySpan<char>>();
        SharqTokenKind kind = lookup.TryGetValue(word, out var k) ? k : SharqTokenKind.Identifier;
        return new SharqToken { Kind = kind, Start = start, Length = length };
    }

    private SharqToken ScanParameter()
    {
        int start = _pos;
        _pos++; // skip $

        // SIMD: scan identifier chars after $
        var remaining = _sql[_pos..];
        int end = remaining.IndexOfAnyExcept(s_identChars);
        _pos += end < 0 ? remaining.Length : end;

        int length = _pos - start;
        // Start+1 / Length-1 to skip the $ in the stored span
        return new SharqToken { Kind = SharqTokenKind.Parameter, Start = start + 1, Length = length - 1 };
    }

    private SharqToken ScanAtOperator()
    {
        int start = _pos;
        _pos++; // skip first @

        if (_pos >= _sql.Length)
            return new SharqToken { Kind = SharqTokenKind.Eof, Start = start, Length = 1 };

        // @@ (simple match)
        if (_sql[_pos] == '@')
        {
            _pos++;
            return new SharqToken { Kind = SharqTokenKind.Match, Start = start, Length = 2 };
        }

        // @AND@ or @OR@
        int wordStart = _pos;
        while (_pos < _sql.Length && char.IsLetter(_sql[_pos]))
            _pos++;

        if (_pos < _sql.Length && _sql[_pos] == '@')
        {
            var keyword = _sql.Slice(wordStart, _pos - wordStart);
            _pos++; // skip closing @

            if (keyword.Equals("AND", StringComparison.OrdinalIgnoreCase))
                return new SharqToken { Kind = SharqTokenKind.MatchAnd, Start = start, Length = _pos - start };
            if (keyword.Equals("OR", StringComparison.OrdinalIgnoreCase))
                return new SharqToken { Kind = SharqTokenKind.MatchOr, Start = start, Length = _pos - start };
        }

        // Unknown @ sequence
        return new SharqToken { Kind = SharqTokenKind.Eof, Start = start, Length = _pos - start };
    }
}
