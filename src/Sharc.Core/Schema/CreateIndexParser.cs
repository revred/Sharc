namespace Sharc.Core.Schema;

/// <summary>
/// Parses CREATE INDEX SQL statements to extract column names and sort order.
/// Manual string parser matching <see cref="CreateTableParser"/> patterns.
/// </summary>
internal static class CreateIndexParser
{
    /// <summary>
    /// Parses column definitions from a CREATE INDEX SQL statement.
    /// </summary>
    /// <param name="sql">The full CREATE INDEX SQL string.</param>
    /// <returns>List of parsed index column info objects.</returns>
    public static IReadOnlyList<IndexColumnInfo> ParseColumns(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return [];

        // Find the column list parentheses after ON table_name
        // Format: CREATE [UNIQUE] INDEX [IF NOT EXISTS] name ON table (col1, col2 DESC, ...)
        int onPos = FindOnKeyword(sql);
        if (onPos < 0)
            return [];

        // Skip past ON + table name to find the opening paren
        int pos = onPos + 2;
        SkipWhitespace(sql, ref pos);
        SkipIdentifier(sql, ref pos); // table name
        SkipWhitespace(sql, ref pos);

        if (pos >= sql.Length || sql[pos] != '(')
            return [];

        int openParen = pos;
        int closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0)
            return [];

        var body = sql.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        var segments = SplitByComma(body);

        var columns = new List<IndexColumnInfo>();
        int ordinal = 0;

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;

            var col = ParseIndexColumn(trimmed, ordinal);
            if (col != null)
            {
                columns.Add(col);
                ordinal++;
            }
        }

        return columns;
    }

    private static int FindOnKeyword(string sql)
    {
        // Search for " ON " (case-insensitive) that's not inside quotes
        for (int i = 0; i < sql.Length - 3; i++)
        {
            if (i > 0 && !char.IsWhiteSpace(sql[i - 1]))
                continue;

            if (sql.AsSpan(i, 2).Equals("ON", StringComparison.OrdinalIgnoreCase) &&
                (i + 2 >= sql.Length || char.IsWhiteSpace(sql[i + 2])))
            {
                return i;
            }
        }
        return -1;
    }

    private static IndexColumnInfo? ParseIndexColumn(string definition, int ordinal)
    {
        int pos = 0;
        string name = ReadIdentifier(definition, ref pos);
        if (name.Length == 0) return null;

        SkipWhitespace(definition, ref pos);

        bool isDescending = false;

        // Check for COLLATE clause — skip it
        if (pos < definition.Length)
        {
            var remaining = definition.AsSpan(pos).TrimStart();
            if (remaining.StartsWith("COLLATE", StringComparison.OrdinalIgnoreCase))
            {
                pos += definition.AsSpan(pos).IndexOf("COLLATE", StringComparison.OrdinalIgnoreCase) + 7;
                SkipWhitespace(definition, ref pos);
                SkipIdentifier(definition, ref pos); // collation name
                SkipWhitespace(definition, ref pos);
            }
        }

        // Check for ASC/DESC
        if (pos < definition.Length)
        {
            var remaining = definition.AsSpan(pos).TrimStart();
            if (remaining.StartsWith("DESC", StringComparison.OrdinalIgnoreCase))
                isDescending = true;
            // ASC is the default — no action needed
        }

        return new IndexColumnInfo
        {
            Name = name,
            Ordinal = ordinal,
            IsDescending = isDescending
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

    private static void SkipIdentifier(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return;

        char first = s[pos];

        if (first is '"' or '[' or '`')
        {
            char closeChar = first switch { '[' => ']', _ => first };
            int end = s.IndexOf(closeChar, pos + 1);
            pos = end < 0 ? s.Length : end + 1;
            return;
        }

        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_'))
            pos++;
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

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
    }
}
