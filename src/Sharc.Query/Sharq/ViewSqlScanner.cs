// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System;
using Sharc.Query.Sharq;

namespace Sharc.Query.Sharq;

/// <summary>
/// Scans a CREATE VIEW statement to extract the inner query body.
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

        // Calculate start of query (immediately after "AS")
        // The token just returned gives us the position of AS.
        // We know the length of AS is 2.
        // But we need the index in the original string.
        // SharqTokenizer doesn't expose strict offsets easily for *subsequent* text without scanning.
        
        // Robust approach: find the "AS" token we just consumed in the original string?
        // No, tokenizer advances.
        
        // Let's rely on the token.Start + token.Length relative to the span.
        // But we need to handle whitespace.
        
        int startPos = asToken.Start + asToken.Length;
        if (startPos >= createViewSql.Length)
            return string.Empty;

        return createViewSql.Substring(startPos).Trim();
    }
}
