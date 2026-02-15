// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Frozen;

namespace Sharc.Query.Sharq.Translate;

/// <summary>
/// Zero-allocation T-SQL → Sharq translator.
/// Single-pass span rewriter that converts T-SQL constructs to Sharq equivalents.
/// </summary>
public static class Tsql
{
    private static readonly FrozenSet<string> s_tableHints = FrozenSet.ToFrozenSet(
        [
            "NOLOCK", "READUNCOMMITTED", "READCOMMITTED", "REPEATABLEREAD",
            "SERIALIZABLE", "HOLDLOCK", "UPDLOCK", "XLOCK",
            "TABLOCK", "TABLOCKX", "ROWLOCK", "PAGLOCK",
            "READPAST", "NOWAIT"
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Translates a T-SQL string to Sharq. Returns the original reference if no changes needed.
    /// </summary>
    public static string Translate(string input)
    {
        Span<char> buffer = input.Length <= 256
            ? stackalloc char[512]
            : new char[input.Length * 2];

        int written = Translate(input.AsSpan(), buffer);

        if (written == input.Length && buffer[..written].SequenceEqual(input.AsSpan()))
            return input;

        return new string(buffer[..written]);
    }

    /// <summary>
    /// Translates T-SQL to Sharq writing into the provided buffer.
    /// Returns the number of characters written.
    /// </summary>
    public static int Translate(ReadOnlySpan<char> input, Span<char> output)
    {
        int r = 0, w = 0;
        int topStart = -1, topLen = 0;
        bool topIsParam = false;
        int fetchValStart = -1, fetchValLen = 0;
        bool fetchIsParam = false;
        int offValStart = -1, offValLen = 0;
        bool offIsParam = false;

        while (r < input.Length)
        {
            char c = input[r];

            // ── String literal: copy verbatim ────────────────────────
            if (c == '\'')
            {
                output[w++] = c; r++;
                while (r < input.Length)
                {
                    output[w++] = input[r];
                    if (input[r] == '\'')
                    {
                        r++;
                        if (r < input.Length && input[r] == '\'')
                        { output[w++] = input[r]; r++; } // escaped ''
                        else break;
                    }
                    else r++;
                }
                continue;
            }

            // ── Quoted identifiers: "...", [...], `...` ──────────────
            if (c == '"' || c == '[' || c == '`')
            {
                char close = c == '[' ? ']' : c;
                output[w++] = c; r++;
                while (r < input.Length)
                {
                    output[w++] = input[r];
                    if (input[r] == close) { r++; break; }
                    r++;
                }
                continue;
            }

            // ── Line comment: -- ... ─────────────────────────────────
            if (c == '-' && r + 1 < input.Length && input[r + 1] == '-')
            {
                while (r < input.Length && input[r] != '\n')
                    output[w++] = input[r++];
                continue;
            }

            // ── Block comment: /* ... */ ─────────────────────────────
            if (c == '/' && r + 1 < input.Length && input[r + 1] == '*')
            {
                output[w++] = input[r++]; output[w++] = input[r++];
                while (r < input.Length)
                {
                    if (input[r] == '*' && r + 1 < input.Length && input[r + 1] == '/')
                    { output[w++] = input[r++]; output[w++] = input[r++]; break; }
                    output[w++] = input[r++];
                }
                continue;
            }

            // ── @param → $param ──────────────────────────────────────
            if (c == '@' && r + 1 < input.Length && (char.IsLetter(input[r + 1]) || input[r + 1] == '_'))
            {
                output[w++] = '$';
                r++; // skip @
                while (r < input.Length && (char.IsLetterOrDigit(input[r]) || input[r] == '_'))
                    output[w++] = input[r++];
                continue;
            }

            // ── Word scanning ────────────────────────────────────────
            if (char.IsLetter(c) || c == '_')
            {
                int ws = r;
                while (r < input.Length && (char.IsLetterOrDigit(input[r]) || input[r] == '_'))
                    r++;
                var word = input[ws..r];

                // N'string' → 'string': strip Unicode prefix
                if (word.Length == 1 && (word[0] == 'N' || word[0] == 'n') && r < input.Length && input[r] == '\'')
                    continue; // skip N, next iteration copies the string

                // SELECT [DISTINCT] [TOP n] handling
                if (word.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    word.CopyTo(output[w..]); w += word.Length;
                    CopyWhitespace(input, output, ref r, ref w);

                    if (!TryPeekWord(input, r, out int nextStart, out int nextEnd))
                        continue;

                    // Optional DISTINCT
                    var nextWord = input[nextStart..nextEnd];
                    if (nextWord.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase))
                    {
                        nextWord.CopyTo(output[w..]); w += nextWord.Length;
                        r = nextEnd;
                        CopyWhitespace(input, output, ref r, ref w);

                        if (!TryPeekWord(input, r, out nextStart, out nextEnd))
                            continue;
                        nextWord = input[nextStart..nextEnd];
                    }

                    // Optional TOP
                    if (nextWord.Equals("TOP", StringComparison.OrdinalIgnoreCase))
                    {
                        r = nextEnd;
                        SkipWhitespace(input, ref r);
                        ExtractTopValue(input, ref r, out topStart, out topLen, out topIsParam);
                        SkipWhitespace(input, ref r);
                    }
                    continue;
                }

                // WITH (NOLOCK) and other table hints → strip
                if (word.Equals("WITH", StringComparison.OrdinalIgnoreCase))
                {
                    int savedR = r;
                    SkipWhitespace(input, ref r);
                    if (r < input.Length && input[r] == '(')
                    {
                        int parenPos = r;
                        r++; // skip (
                        SkipWhitespace(input, ref r);
                        if (TryPeekWord(input, r, out int hintStart, out int hintEnd) &&
                            IsTableHint(input[hintStart..hintEnd]))
                        {
                            // Skip entire WITH (...) clause
                            while (r < input.Length && input[r] != ')')
                                r++;
                            if (r < input.Length) r++; // skip )
                            // Trim trailing space before WITH from output
                            if (w > 0 && output[w - 1] == ' ') w--;
                            continue;
                        }
                        r = savedR; // not a hint, restore
                    }
                    else
                    {
                        r = savedR;
                    }
                    word.CopyTo(output[w..]); w += word.Length;
                    continue;
                }

                // OFFSET n ROWS FETCH NEXT m ROWS ONLY → capture for LIMIT m OFFSET n
                if (word.Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
                {
                    int savedR = r;
                    if (TryParseOffsetFetch(input, ref r,
                        out offValStart, out offValLen, out offIsParam,
                        out fetchValStart, out fetchValLen, out fetchIsParam))
                    {
                        // Trim trailing space before OFFSET from output
                        if (w > 0 && output[w - 1] == ' ') w--;
                        continue; // values captured, will append at end
                    }
                    r = savedR; // not T-SQL pattern, restore
                    fetchValStart = -1; // ensure end-of-pass check doesn't trigger
                    word.CopyTo(output[w..]); w += word.Length;
                    continue;
                }

                // Default: copy word through
                word.CopyTo(output[w..]); w += word.Length;
                continue;
            }

            // ── Default: copy char ───────────────────────────────────
            output[w++] = c; r++;
        }

        // ── Append LIMIT if TOP was captured ─────────────────────────
        if (topStart >= 0)
        {
            bool hasSemicolon = w > 0 && output[w - 1] == ';';
            if (hasSemicolon) w--;

            " LIMIT ".AsSpan().CopyTo(output[w..]); w += 7;

            if (topIsParam)
                output[w++] = '$';

            input.Slice(topStart, topLen).CopyTo(output[w..]); w += topLen;

            if (hasSemicolon) output[w++] = ';';
        }

        // ── Append LIMIT m OFFSET n if OFFSET FETCH was captured ─────
        if (fetchValStart >= 0)
        {
            bool hasSemicolon = w > 0 && output[w - 1] == ';';
            if (hasSemicolon) w--;

            " LIMIT ".AsSpan().CopyTo(output[w..]); w += 7;
            if (fetchIsParam) output[w++] = '$';
            input.Slice(fetchValStart, fetchValLen).CopyTo(output[w..]); w += fetchValLen;

            " OFFSET ".AsSpan().CopyTo(output[w..]); w += 8;
            if (offIsParam) output[w++] = '$';
            input.Slice(offValStart, offValLen).CopyTo(output[w..]); w += offValLen;

            if (hasSemicolon) output[w++] = ';';
        }

        return w;
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static void CopyWhitespace(ReadOnlySpan<char> input, Span<char> output, ref int r, ref int w)
    {
        while (r < input.Length && char.IsWhiteSpace(input[r]))
            output[w++] = input[r++];
    }

    private static void SkipWhitespace(ReadOnlySpan<char> input, ref int r)
    {
        while (r < input.Length && char.IsWhiteSpace(input[r]))
            r++;
    }

    private static bool TryPeekWord(ReadOnlySpan<char> input, int pos,
        out int wordStart, out int wordEnd)
    {
        if (pos < input.Length && (char.IsLetter(input[pos]) || input[pos] == '_'))
        {
            wordStart = pos;
            while (pos < input.Length && (char.IsLetterOrDigit(input[pos]) || input[pos] == '_'))
                pos++;
            wordEnd = pos;
            return true;
        }
        wordStart = wordEnd = pos;
        return false;
    }

    private static bool IsTableHint(ReadOnlySpan<char> word) =>
        s_tableHints.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word);

    /// <summary>
    /// Tries to parse T-SQL OFFSET n ROWS FETCH NEXT/FIRST m ROWS [ONLY] pattern.
    /// On success, advances r past the entire clause and outputs fetch/offset value positions.
    /// On failure, r is NOT restored — caller must save/restore.
    /// </summary>
    private static bool TryParseOffsetFetch(ReadOnlySpan<char> input, ref int r,
        out int offStart, out int offLen, out bool offParam,
        out int fetchStart, out int fetchLen, out bool fetchParam)
    {
        offStart = offLen = fetchStart = fetchLen = 0;
        offParam = fetchParam = false;

        SkipWhitespace(input, ref r);

        // Offset value: number or @param
        if (!ExtractValue(input, ref r, out offStart, out offLen, out offParam))
            return false;

        SkipWhitespace(input, ref r);

        // ROWS or ROW
        if (!TryPeekWord(input, r, out int rwStart, out int rwEnd))
            return false;
        var rw = input[rwStart..rwEnd];
        if (!rw.Equals("ROWS", StringComparison.OrdinalIgnoreCase) &&
            !rw.Equals("ROW", StringComparison.OrdinalIgnoreCase))
            return false;
        r = rwEnd;
        SkipWhitespace(input, ref r);

        // FETCH
        if (!TryPeekWord(input, r, out int fStart, out int fEnd) ||
            !input[fStart..fEnd].Equals("FETCH", StringComparison.OrdinalIgnoreCase))
            return false;
        r = fEnd;
        SkipWhitespace(input, ref r);

        // NEXT or FIRST
        if (!TryPeekWord(input, r, out int nStart, out int nEnd))
            return false;
        var nw = input[nStart..nEnd];
        if (!nw.Equals("NEXT", StringComparison.OrdinalIgnoreCase) &&
            !nw.Equals("FIRST", StringComparison.OrdinalIgnoreCase))
            return false;
        r = nEnd;
        SkipWhitespace(input, ref r);

        // Fetch value: number or @param
        if (!ExtractValue(input, ref r, out fetchStart, out fetchLen, out fetchParam))
            return false;

        SkipWhitespace(input, ref r);

        // ROWS or ROW
        if (TryPeekWord(input, r, out int rw2Start, out int rw2End))
        {
            var rw2 = input[rw2Start..rw2End];
            if (rw2.Equals("ROWS", StringComparison.OrdinalIgnoreCase) ||
                rw2.Equals("ROW", StringComparison.OrdinalIgnoreCase))
            {
                r = rw2End;
                SkipWhitespace(input, ref r);
            }
        }

        // Optional ONLY
        if (TryPeekWord(input, r, out int oStart, out int oEnd) &&
            input[oStart..oEnd].Equals("ONLY", StringComparison.OrdinalIgnoreCase))
            r = oEnd;

        return true;
    }

    private static bool ExtractValue(ReadOnlySpan<char> input, ref int r,
        out int valStart, out int valLen, out bool isParam)
    {
        isParam = false;
        valStart = valLen = 0;

        if (r < input.Length && input[r] == '@')
        {
            isParam = true;
            r++; // skip @
            valStart = r;
            while (r < input.Length && (char.IsLetterOrDigit(input[r]) || input[r] == '_'))
                r++;
            valLen = r - valStart;
            return valLen > 0;
        }

        if (r < input.Length && char.IsAsciiDigit(input[r]))
        {
            valStart = r;
            while (r < input.Length && char.IsAsciiDigit(input[r]))
                r++;
            valLen = r - valStart;
            return true;
        }

        return false;
    }

    private static void ExtractTopValue(ReadOnlySpan<char> input, ref int r,
        out int topStart, out int topLen, out bool isParam)
    {
        isParam = false;

        if (r < input.Length && input[r] == '(')
        {
            r++; // skip (
            SkipWhitespace(input, ref r);

            if (r < input.Length && input[r] == '@')
            {
                isParam = true;
                r++; // skip @
            }

            topStart = r;
            while (r < input.Length && input[r] != ')')
                r++;
            topLen = r - topStart;

            // Trim trailing whitespace from captured value
            while (topLen > 0 && char.IsWhiteSpace(input[topStart + topLen - 1]))
                topLen--;

            if (r < input.Length) r++; // skip )
        }
        else
        {
            topStart = r;
            while (r < input.Length && char.IsAsciiDigit(input[r]))
                r++;
            topLen = r - topStart;
        }
    }
}
