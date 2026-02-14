// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.



/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/


namespace Sharc.Core.Schema;

/// <summary>
/// Parses CREATE TABLE SQL statements to extract column information.
/// Manual string parser â€” no regex. Handles quoted identifiers and table constraints.
/// optimized for zero allocation using ReadOnlySpan&lt;char&gt;.
/// </summary>
internal static class CreateTableParser
{
    /// <summary>
    /// Parses column definitions from a CREATE TABLE SQL statement.
    /// </summary>
    /// <param name="sql">The full CREATE TABLE SQL string as a span.</param>
    /// <returns>List of parsed column info objects.</returns>
    public static IReadOnlyList<ColumnInfo> ParseColumns(ReadOnlySpan<char> sql)
    {
        // Find the opening parenthesis
        int openParen = sql.IndexOf('(');
        if (openParen < 0)
            return Array.Empty<ColumnInfo>();

        // Find the matching closing parenthesis
        int closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0)
            return Array.Empty<ColumnInfo>();

        var body = sql.Slice(openParen + 1, closeParen - openParen - 1).Trim();
        
        var columns = new List<ColumnInfo>();
        int ordinal = 0;
        int pos = 0;

        while (pos < body.Length)
        {
            var segment = ReadNextSegment(body, ref pos);
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;

            // Skip table-level constraints
            if (IsTableConstraint(trimmed)) continue;

            var col = ParseColumnDefinition(trimmed, ordinal);
            if (col != null)
            {
                columns.Add(col);
                ordinal++;
            }
        }

        return columns;
    }

    private static int FindMatchingParen(ReadOnlySpan<char> sql, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        
        for (int i = openIndex; i < sql.Length; i++)
        {
            char c = sql[i];
            if (c == '\'' && !inString) { inString = true; continue; }
            if (c == '\'' && inString) { inString = false; continue; }
            if (inString) continue;

            if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>
    /// Reads the next comma-separated segment, respecting parenthesized groups and strings.
    /// </summary>
    private static ReadOnlySpan<char> ReadNextSegment(ReadOnlySpan<char> body, ref int pos)
    {
        int depth = 0;
        bool inString = false;
        int start = pos;

        for (; pos < body.Length; pos++)
        {
            char c = body[pos];
            if (c == '\'' && !inString) { inString = true; continue; }
            if (c == '\'' && inString) { inString = false; continue; }
            if (inString) continue;

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                var segment = body.Slice(start, pos - start);
                pos++; // Skip comma
                return segment;
            }
        }

        // End of string
        return body.Slice(start);
    }

    private static bool IsTableConstraint(ReadOnlySpan<char> segment)
    {
        var span = segment.TrimStart();
        return span.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
    }

    private static ColumnInfo? ParseColumnDefinition(ReadOnlySpan<char> definition, int ordinal)
    {
        int pos = 0;
        string name = ReadIdentifier(definition, ref pos);
        if (name.Length == 0) return null;

        SkipWhitespace(definition, ref pos);

        // Read type name (everything up to a constraint keyword or end)
        string declaredType = ReadTypeName(definition, ref pos);

        // Check remaining for constraints
        // We scan the rest of the definition for keywords
        var remaining = pos < definition.Length ? definition.Slice(pos) : ReadOnlySpan<char>.Empty;

        bool isPrimaryKey = Contains(remaining, "PRIMARY KEY");
        bool isNotNull = Contains(remaining, "NOT NULL");

        return new ColumnInfo
        {
            Name = name,
            DeclaredType = declaredType,
            Ordinal = ordinal,
            IsPrimaryKey = isPrimaryKey,
            IsNotNull = isNotNull || isPrimaryKey // PK is implicitly NOT NULL
        };
    }
    
    private static bool Contains(ReadOnlySpan<char> span, string value)
    {
        return span.IndexOf(value.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ReadIdentifier(ReadOnlySpan<char> s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return "";

        char first = s[pos];

        // Quoted identifier: "name", [name], `name`
        if (first is '"' or '[' or '`')
        {
            char closeChar = first switch { '[' => ']', _ => first };
            int start = pos + 1;
            int end = -1;
            
            // Find close char
            for(int i = start; i < s.Length; i++)
            {
                if(s[i] == closeChar)
                {
                    end = i;
                    break;
                }
            }
            
            if (end < 0) end = s.Length;
            else pos = end + 1; // Advance past quote
            
            return s.Slice(start, end - start).ToString();
        }

        // Unquoted identifier
        int idStart = pos;
        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_'))
            pos++;
            
        return s.Slice(idStart, pos - idStart).ToString();
    }

    private static string ReadTypeName(ReadOnlySpan<char> s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return "";

        // Type name can include words and parenthesized arguments like VARCHAR(255)
        int start = pos;
        int depth = 0;

        while (pos < s.Length)
        {
            char c = s[pos];

            if (c == '(') { depth++; pos++; continue; }
            if (c == ')') { depth--; pos++; if (depth == 0) break; continue; }
            if (depth > 0) { pos++; continue; }

            // Stop at constraint keywords
            // Check if current position starts a keyword
            var remaining = s.Slice(pos).TrimStart();
            if (remaining.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("NOT", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("REFERENCES", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("COLLATE", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("GENERATED", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
                break;

            pos++;
        }

        return s.Slice(start, pos - start).Trim().ToString();
    }

    private static void SkipWhitespace(ReadOnlySpan<char> s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
    }
}