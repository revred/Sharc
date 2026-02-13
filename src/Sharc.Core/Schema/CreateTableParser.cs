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


namespace Sharc.Core.Schema;

/// <summary>
/// Parses CREATE TABLE SQL statements to extract column information.
/// Manual string parser â€” no regex. Handles quoted identifiers and table constraints.
/// </summary>
internal static class CreateTableParser
{
    /// <summary>
    /// Parses column definitions from a CREATE TABLE SQL statement.
    /// </summary>
    /// <param name="sql">The full CREATE TABLE SQL string.</param>
    /// <returns>List of parsed column info objects.</returns>
    public static IReadOnlyList<ColumnInfo> ParseColumns(string sql)
    {
        // Find the opening parenthesis
        int openParen = sql.IndexOf('(');
        if (openParen < 0)
            return [];

        // Find the matching closing parenthesis
        int closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0)
            return [];

        var body = sql.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        var segments = SplitByComma(body);

        var columns = new List<ColumnInfo>();
        int ordinal = 0;

        foreach (var segment in segments)
        {
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

    private static int FindMatchingParen(string sql, int openIndex)
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
    /// Splits a string by commas, respecting parenthesized groups and string literals.
    /// </summary>
    private static List<string> SplitByComma(string body)
    {
        var result = new List<string>();
        int depth = 0;
        bool inString = false;
        int start = 0;

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\'' && !inString) { inString = true; continue; }
            if (c == '\'' && inString) { inString = false; continue; }
            if (inString) continue;

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(body[start..i]);
                start = i + 1;
            }
        }

        if (start < body.Length)
            result.Add(body[start..]);

        return result;
    }

    private static bool IsTableConstraint(string segment)
    {
        var span = segment.AsSpan().TrimStart();
        return span.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
    }

    private static ColumnInfo? ParseColumnDefinition(string definition, int ordinal)
    {
        int pos = 0;
        string name = ReadIdentifier(definition, ref pos);
        if (name.Length == 0) return null;

        SkipWhitespace(definition, ref pos);

        // Read type name (everything up to a constraint keyword or end)
        string declaredType = ReadTypeName(definition, ref pos);

        // Check remaining for constraints
        string remaining = pos < definition.Length ? definition[pos..] : "";

        var definitionSpan = definition.AsSpan();
        bool isPrimaryKey = definitionSpan.Contains("PRIMARY KEY".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool isNotNull = definitionSpan.Contains("NOT NULL".AsSpan(), StringComparison.OrdinalIgnoreCase);

        return new ColumnInfo
        {
            Name = name,
            DeclaredType = declaredType,
            Ordinal = ordinal,
            IsPrimaryKey = isPrimaryKey,
            IsNotNull = isNotNull || isPrimaryKey // PK is implicitly NOT NULL
        };
    }

    private static string ReadIdentifier(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return "";

        char first = s[pos];

        // Quoted identifier: "name", [name], `name`
        if (first is '"' or '[' or '`')
        {
            char closeChar = first switch { '[' => ']', _ => first };
            int start = pos + 1;
            int end = s.IndexOf(closeChar, start);
            if (end < 0) end = s.Length;
            pos = end + 1;
            return s[start..end];
        }

        // Unquoted identifier
        int idStart = pos;
        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_'))
            pos++;
        return s[idStart..pos];
    }

    private static string ReadTypeName(string s, ref int pos)
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
            var remaining = s.AsSpan(pos).TrimStart();
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

        return s[start..pos].Trim();
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
    }
}
