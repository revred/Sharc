// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Schema;

/// <summary>
/// Unified parser for CREATE TABLE and CREATE INDEX SQL statements.
/// Consolidates identifier and parenthesis handling to minimize binary footprint.
/// </summary>
internal static class SchemaParser
{
    public static IReadOnlyList<ColumnInfo> ParseTableColumns(ReadOnlySpan<char> sql)
    {
        int openParen = sql.IndexOf('(');
        if (openParen < 0) return [];
        int closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0) return [];

        var body = sql.Slice(openParen + 1, closeParen - openParen - 1).Trim();
        var columns = new List<ColumnInfo>(8); // typical table has 5-10 columns
        int ordinal = 0, pos = 0;

        while (pos < body.Length)
        {
            var segment = ReadNextSegment(body, ref pos);
            var trimmed = segment.Trim();
            if (trimmed.Length == 0 || IsTableConstraint(trimmed)) continue;

            var col = ParseColumnDefinition(trimmed, ordinal++);
            if (col != null) columns.Add(col);
        }
        return columns;
    }

    public static IReadOnlyList<IndexColumnInfo> ParseIndexColumns(ReadOnlySpan<char> sql)
    {
        int onPos = IndexOfKeyword(sql, "ON");
        if (onPos < 0) return [];

        int pos = onPos + 2;
        SkipWhitespace(sql, ref pos);
        SkipIdentifier(sql, ref pos); // table name
        SkipWhitespace(sql, ref pos);

        if (pos >= sql.Length || sql[pos] != '(') return [];

        int openParen = pos;
        int closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0) return [];

        var body = sql.Slice(openParen + 1, closeParen - openParen - 1).Trim();
        var columns = new List<IndexColumnInfo>(4); // typical index has 1-3 columns
        int ordinal = 0, bPos = 0;

        while (bPos < body.Length)
        {
            var segment = ReadNextSegment(body, ref bPos);
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;

            var col = ParseIndexColumn(trimmed, ordinal++);
            if (col != null) columns.Add(col);
        }
        return columns;
    }

    private static ColumnInfo? ParseColumnDefinition(ReadOnlySpan<char> definition, int ordinal)
    {
        int pos = 0;
        string name = ReadIdentifier(definition, ref pos);
        if (name.Length == 0) return null;
        string type = ReadUntilKeyword(definition, ref pos);
        var rem = definition.Slice(pos);
        return new ColumnInfo {
            Name = name, DeclaredType = type, Ordinal = ordinal,
            IsPrimaryKey = Contains(rem, "PRIMARY KEY"),
            IsNotNull = Contains(rem, "NOT NULL") || Contains(rem, "PRIMARY KEY"),
            IsGuidColumn = type.Equals("GUID", StringComparison.OrdinalIgnoreCase) ||
                           type.Equals("UUID", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static IndexColumnInfo? ParseIndexColumn(ReadOnlySpan<char> definition, int ordinal)
    {
        int pos = 0;
        string name = ReadIdentifier(definition, ref pos);
        if (name.Length == 0) return null;
        var rem = definition.Slice(pos);
        return new IndexColumnInfo { 
            Name = name, Ordinal = ordinal,
            IsDescending = Contains(rem, "DESC")
        };
    }

    /// <summary>
    /// Detects __hi/__lo column pairs and merges them into logical GUID columns.
    /// Columns named "foo__hi" + "foo__lo" become a single "foo" of type GUID.
    /// </summary>
    public static (IReadOnlyList<ColumnInfo> LogicalColumns, int PhysicalColumnCount) MergeColumnPairs(
        IReadOnlyList<ColumnInfo> physicalColumns)
    {
        if (physicalColumns.Count < 2)
            return (physicalColumns, physicalColumns.Count);

        // Build a set of __lo names for quick lookup
        var loNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < physicalColumns.Count; i++)
        {
            if (physicalColumns[i].Name.EndsWith("__lo", StringComparison.OrdinalIgnoreCase))
                loNames.Add(physicalColumns[i].Name);
        }

        if (loNames.Count == 0)
            return (physicalColumns, physicalColumns.Count);

        var consumed = new HashSet<int>(loNames.Count * 2); // physical ordinals consumed by merges
        var result = new List<ColumnInfo>(physicalColumns.Count);
        int logicalOrdinal = 0;

        for (int i = 0; i < physicalColumns.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var col = physicalColumns[i];
            if (col.Name.EndsWith("__hi", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = col.Name[..^4]; // strip __hi
                string expectedLo = baseName + "__lo";

                // Find matching __lo
                int loIndex = -1;
                for (int j = i + 1; j < physicalColumns.Count; j++)
                {
                    if (physicalColumns[j].Name.Equals(expectedLo, StringComparison.OrdinalIgnoreCase))
                    {
                        loIndex = j;
                        break;
                    }
                }

                if (loIndex >= 0)
                {
                    var loCol = physicalColumns[loIndex];
                    consumed.Add(i);
                    consumed.Add(loIndex);
                    result.Add(new ColumnInfo
                    {
                        Name = baseName,
                        DeclaredType = "GUID",
                        Ordinal = logicalOrdinal++,
                        IsPrimaryKey = col.IsPrimaryKey && loCol.IsPrimaryKey,
                        IsNotNull = col.IsNotNull && loCol.IsNotNull,
                        IsGuidColumn = true,
                        MergedPhysicalOrdinals = [col.Ordinal, loCol.Ordinal]
                    });
                    continue;
                }
            }

            // Regular column — pass through with updated ordinal.
            // Store the original physical ordinal so the reader can map
            // logical → physical when merged columns shift ordinals.
            result.Add(new ColumnInfo
            {
                Name = col.Name,
                DeclaredType = col.DeclaredType,
                Ordinal = logicalOrdinal++,
                IsPrimaryKey = col.IsPrimaryKey,
                IsNotNull = col.IsNotNull,
                IsGuidColumn = col.IsGuidColumn,
                MergedPhysicalOrdinals = [col.Ordinal]
            });
        }

        return (result, physicalColumns.Count);
    }

    private static int FindMatchingParen(ReadOnlySpan<char> sql, int openIndex)
    {
        int depth = 0; bool inStr = false;
        for (int i = openIndex; i < sql.Length; i++) {
            if (sql[i] == '\'') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (sql[i] == '(') depth++;
            else if (sql[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static ReadOnlySpan<char> ReadNextSegment(ReadOnlySpan<char> body, ref int pos)
    {
        int depth = 0; bool inStr = false; int start = pos;
        for (; pos < body.Length; pos++) {
            if (body[pos] == '\'') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (body[pos] == '(') depth++;
            else if (body[pos] == ')') depth--;
            else if (body[pos] == ',' && depth == 0) {
                var seg = body.Slice(start, pos - start); pos++; return seg;
            }
        }
        return body.Slice(start);
    }

    private static string ReadIdentifier(ReadOnlySpan<char> s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return "";
        if (s[pos] is '"' or '[' or '`') {
            char endChar = s[pos] == '[' ? ']' : s[pos];
            int start = ++pos;
            while (pos < s.Length && s[pos] != endChar) pos++;
            string id = s.Slice(start, pos - start).ToString();
            if (pos < s.Length) pos++; return id;
        }
        int idS = pos;
        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
        return s.Slice(idS, pos - idS).ToString();
    }

    private static string ReadUntilKeyword(ReadOnlySpan<char> s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        int start = pos, depth = 0;
        while (pos < s.Length) {
            if (s[pos] == '(') depth++;
            else if (s[pos] == ')') { depth--; pos++; if (depth == 0) break; continue; }
            if (depth == 0 && IsKeywordAt(s, pos)) break;
            pos++;
        }
        return s.Slice(start, pos - start).Trim().ToString();
    }

    private static bool IsKeywordAt(ReadOnlySpan<char> s, int pos) {
        var rem = s.Slice(pos).TrimStart();
        return rem.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
               rem.StartsWith("NOT", StringComparison.OrdinalIgnoreCase) ||
               rem.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
               rem.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase) ||
               rem.StartsWith("REFERENCES", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTableConstraint(ReadOnlySpan<char> s) =>
        s.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase);

    private static int IndexOfKeyword(ReadOnlySpan<char> s, string kw) {
        for (int i = 0; i <= s.Length - kw.Length; i++) {
            if ((i == 0 || char.IsWhiteSpace(s[i - 1])) && 
                s.Slice(i, kw.Length).Equals(kw.AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                (i + kw.Length == s.Length || char.IsWhiteSpace(s[i + kw.Length]))) return i;
        }
        return -1;
    }

    private static bool Contains(ReadOnlySpan<char> s, string v) => s.IndexOf(v.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
    private static void SkipWhitespace(ReadOnlySpan<char> s, ref int pos) { while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++; }
    private static void SkipIdentifier(ReadOnlySpan<char> s, ref int pos) => ReadIdentifier(s, ref pos);
}
