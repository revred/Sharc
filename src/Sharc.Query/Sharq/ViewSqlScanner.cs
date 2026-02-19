// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Sharc.Query.Sharq;

/// <summary>
/// Scans a CREATE VIEW statement to extract the inner query body
/// and structural metadata (source tables, columns, flags).
/// </summary>
internal static class ViewSqlScanner
{
    /// <summary>
    /// Extracts the definition SQL (SELECT ...) from a CREATE VIEW statement.
    /// Expects "CREATE VIEW name AS SELECT ...".
    /// </summary>
    public static string ExtractQuery(string createViewSql)
    {
        var tokenizer = new SharqTokenizer(createViewSql);

        // 1. CREATE
        if (tokenizer.NextToken().Kind != SharqTokenKind.Create)
            throw new ArgumentException("Invalid view definition: expected CREATE.");

        // 2. VIEW
        if (tokenizer.NextToken().Kind != SharqTokenKind.View)
             throw new ArgumentException("Invalid view definition: expected VIEW.");

        // 3. Name (identifier)
        if (tokenizer.NextToken().Kind != SharqTokenKind.Identifier)
             throw new ArgumentException("Invalid view definition: expected view name.");

        // 4. AS
        var asToken = tokenizer.NextToken();
        if (asToken.Kind != SharqTokenKind.As)
             throw new ArgumentException("Invalid view definition: expected AS.");

        int startPos = asToken.Start + asToken.Length;
        if (startPos >= createViewSql.Length)
            return string.Empty;

        return createViewSql.Substring(startPos).Trim();
    }

    /// <summary>
    /// Extracts structural metadata from a CREATE VIEW SQL statement.
    /// Forward-only token scan — not a full SQL parser. Handles ~85% of real-world views.
    /// </summary>
    public static ViewParseResult Scan(string createViewSql)
    {
        if (string.IsNullOrWhiteSpace(createViewSql))
            return ViewParseResult.Failed;

        var tokenizer = new SharqTokenizer(createViewSql);

        // Skip CREATE
        var tok = tokenizer.NextToken();
        if (tok.Kind != SharqTokenKind.Create)
            return ViewParseResult.Failed;

        // Skip optional TEMP/TEMPORARY
        tok = tokenizer.NextToken();
        if (tok.Kind == SharqTokenKind.Identifier)
        {
            var word = tokenizer.GetText(tok);
            if (word.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("TEMPORARY", StringComparison.OrdinalIgnoreCase))
                tok = tokenizer.NextToken();
        }

        // VIEW
        if (tok.Kind != SharqTokenKind.View)
            return ViewParseResult.Failed;

        // Skip optional IF NOT EXISTS
        tok = tokenizer.Peek();
        if (tok.Kind == SharqTokenKind.Identifier)
        {
            var word = tokenizer.GetText(tok);
            if (word.Equals("IF", StringComparison.OrdinalIgnoreCase))
            {
                tokenizer.NextToken(); // consume IF
                tokenizer.NextToken(); // NOT
                tokenizer.NextToken(); // EXISTS
            }
        }

        // View name (identifier)
        tok = tokenizer.NextToken();
        if (tok.Kind != SharqTokenKind.Identifier)
            return ViewParseResult.Failed;

        // AS
        tok = tokenizer.NextToken();
        if (tok.Kind != SharqTokenKind.As)
            return ViewParseResult.Failed;

        // Now parse the SELECT body
        tok = tokenizer.NextToken();
        if (tok.Kind != SharqTokenKind.Select)
            return ViewParseResult.Failed;

        // Check for DISTINCT
        tok = tokenizer.NextToken();
        if (tok.Kind == SharqTokenKind.Distinct)
            tok = tokenizer.NextToken();

        // Parse SELECT list
        bool isSelectAll = false;
        var columns = new List<ViewColumnRef>();

        if (tok.Kind == SharqTokenKind.Star)
        {
            isSelectAll = true;
            tok = tokenizer.NextToken(); // advance past *
        }
        else
        {
            // Parse column list until FROM
            int ordinal = 0;
            while (tok.Kind != SharqTokenKind.From && tok.Kind != SharqTokenKind.Eof)
            {
                if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
                {
                    string sourceName = tokenizer.GetText(tok).ToString();
                    string displayName = sourceName;

                    // Check for table.column
                    var next = tokenizer.Peek();
                    if (next.Kind == SharqTokenKind.Dot)
                    {
                        tokenizer.NextToken(); // consume dot
                        tok = tokenizer.NextToken(); // column name
                        if (tok.Kind == SharqTokenKind.Identifier || tok.Kind == SharqTokenKind.Star
                            || IsKeywordUsableAsIdent(tok.Kind))
                        {
                            if (tok.Kind == SharqTokenKind.Star)
                            {
                                // table.* pattern
                                isSelectAll = true;
                                tok = tokenizer.NextToken();
                                if (tok.Kind == SharqTokenKind.Comma)
                                    tok = tokenizer.NextToken();
                                continue;
                            }
                            sourceName = tokenizer.GetText(tok).ToString();
                            displayName = sourceName;
                        }
                    }

                    // Check for AS alias
                    next = tokenizer.Peek();
                    if (next.Kind == SharqTokenKind.As)
                    {
                        tokenizer.NextToken(); // consume AS
                        tok = tokenizer.NextToken(); // alias
                        if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
                            displayName = tokenizer.GetText(tok).ToString();
                    }

                    columns.Add(new ViewColumnRef(sourceName, displayName, ordinal));
                    ordinal++;
                }
                else if (tok.Kind == SharqTokenKind.LeftParen)
                {
                    // Skip parenthesized expression (subquery or function call)
                    int depth = 1;
                    while (depth > 0)
                    {
                        tok = tokenizer.NextToken();
                        if (tok.Kind == SharqTokenKind.Eof) return ViewParseResult.Failed;
                        if (tok.Kind == SharqTokenKind.LeftParen) depth++;
                        if (tok.Kind == SharqTokenKind.RightParen) depth--;
                    }
                    // Check for AS alias after the parenthesized expr
                    var next = tokenizer.Peek();
                    if (next.Kind == SharqTokenKind.As)
                    {
                        tokenizer.NextToken(); // consume AS
                        tok = tokenizer.NextToken(); // alias
                        if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
                        {
                            var alias = tokenizer.GetText(tok).ToString();
                            columns.Add(new ViewColumnRef(alias, alias, ordinal));
                            ordinal++;
                        }
                    }
                    else
                    {
                        columns.Add(new ViewColumnRef("expr" + ordinal, "expr" + ordinal, ordinal));
                        ordinal++;
                    }
                }

                tok = tokenizer.NextToken();
                if (tok.Kind == SharqTokenKind.Comma)
                    tok = tokenizer.NextToken(); // advance past comma
            }
        }

        // Parse FROM clause — extract source tables
        var sourceTables = new List<string>();
        bool hasJoin = false;
        bool hasFilter = false;

        if (tok.Kind == SharqTokenKind.From)
        {
            tok = tokenizer.NextToken();

            // First table
            if (tok.Kind == SharqTokenKind.LeftParen)
            {
                // Subquery in FROM — skip it
                SkipParenthesized(ref tokenizer, ref tok);
                SkipOptionalAlias(ref tokenizer, ref tok);
            }
            else if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
            {
                sourceTables.Add(tokenizer.GetText(tok).ToString());
                tok = tokenizer.NextToken();
                SkipOptionalAlias(ref tokenizer, ref tok);
            }

            // Parse remaining clauses — look for JOINs, WHERE, additional tables
            while (tok.Kind != SharqTokenKind.Eof)
            {
                if (tok.Kind == SharqTokenKind.Comma)
                {
                    // Cross join via comma
                    tok = tokenizer.NextToken();
                    if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
                    {
                        sourceTables.Add(tokenizer.GetText(tok).ToString());
                        tok = tokenizer.NextToken();
                        SkipOptionalAlias(ref tokenizer, ref tok);
                        continue;
                    }
                }

                if (tok.Kind is SharqTokenKind.Join or SharqTokenKind.Inner
                    or SharqTokenKind.Left or SharqTokenKind.Right or SharqTokenKind.Cross)
                {
                    hasJoin = true;
                    // Advance to JOIN keyword if we're at a modifier (INNER/LEFT/RIGHT/CROSS)
                    if (tok.Kind != SharqTokenKind.Join)
                        tok = tokenizer.NextToken(); // should be JOIN
                    if (tok.Kind == SharqTokenKind.Join)
                        tok = tokenizer.NextToken(); // table name
                    if (tok.Kind == SharqTokenKind.Identifier || IsKeywordUsableAsIdent(tok.Kind))
                    {
                        sourceTables.Add(tokenizer.GetText(tok).ToString());
                        tok = tokenizer.NextToken();
                        SkipOptionalAlias(ref tokenizer, ref tok);
                    }
                    continue;
                }

                if (tok.Kind == SharqTokenKind.Where)
                {
                    hasFilter = true;
                }

                tok = tokenizer.NextToken();
            }
        }

        return new ViewParseResult(
            sourceTables.ToArray(),
            columns.ToArray(),
            isSelectAll,
            hasJoin,
            hasFilter,
            parseSucceeded: true);
    }

    /// <summary>
    /// Skip a parenthesized expression (balanced parens).
    /// On exit, tok is the token AFTER the closing paren.
    /// </summary>
    private static void SkipParenthesized(ref SharqTokenizer tokenizer, ref SharqToken tok)
    {
        int depth = 1;
        while (depth > 0)
        {
            tok = tokenizer.NextToken();
            if (tok.Kind == SharqTokenKind.Eof) return;
            if (tok.Kind == SharqTokenKind.LeftParen) depth++;
            if (tok.Kind == SharqTokenKind.RightParen) depth--;
        }
        tok = tokenizer.NextToken();
    }

    /// <summary>
    /// Skip an optional alias (AS ident or just ident) after a table reference.
    /// </summary>
    private static void SkipOptionalAlias(ref SharqTokenizer tokenizer, ref SharqToken tok)
    {
        if (tok.Kind == SharqTokenKind.As)
        {
            tokenizer.NextToken(); // consume alias
            tok = tokenizer.NextToken();
        }
        else if (tok.Kind == SharqTokenKind.Identifier)
        {
            tok = tokenizer.NextToken();
        }
    }

    /// <summary>
    /// Returns true for keywords that can be used as identifiers in column/table positions.
    /// </summary>
    private static bool IsKeywordUsableAsIdent(SharqTokenKind kind) =>
        kind is SharqTokenKind.Null or SharqTokenKind.True or SharqTokenKind.False
            or SharqTokenKind.Asc or SharqTokenKind.Desc;
}

/// <summary>
/// Result of scanning a CREATE VIEW statement for structural metadata.
/// </summary>
internal readonly struct ViewParseResult
{
    public readonly string[] SourceTables;
    public readonly ViewColumnRef[] Columns;
    public readonly bool IsSelectAll;
    public readonly bool HasJoin;
    public readonly bool HasFilter;
    public readonly bool ParseSucceeded;

    public ViewParseResult(
        string[] sourceTables,
        ViewColumnRef[] columns,
        bool isSelectAll,
        bool hasJoin,
        bool hasFilter,
        bool parseSucceeded)
    {
        SourceTables = sourceTables;
        Columns = columns;
        IsSelectAll = isSelectAll;
        HasJoin = hasJoin;
        HasFilter = hasFilter;
        ParseSucceeded = parseSucceeded;
    }

    internal static readonly ViewParseResult Failed = new(
        Array.Empty<string>(),
        Array.Empty<ViewColumnRef>(),
        isSelectAll: false,
        hasJoin: false,
        hasFilter: false,
        parseSucceeded: false);
}

/// <summary>
/// A column reference within a parsed view definition.
/// </summary>
internal readonly struct ViewColumnRef
{
    public readonly string SourceName;
    public readonly string DisplayName;
    public readonly int Ordinal;

    public ViewColumnRef(string sourceName, string displayName, int ordinal)
    {
        SourceName = sourceName;
        DisplayName = displayName;
        Ordinal = ordinal;
    }
}
