// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;

namespace Sharc.Vector;

/// <summary>
/// Lightweight term-frequency (TF) scorer for keyword relevance.
/// Counts occurrences of query terms in text, normalized by text length.
/// </summary>
/// <remarks>
/// This is not a full BM25 implementation. It provides simple, meaningful
/// ranking that goes beyond boolean match/no-match. When full FTS is built,
/// it can replace this scorer.
/// </remarks>
internal static class TextScorer
{
    /// <summary>
    /// Computes a term-frequency score for the given text against query terms.
    /// </summary>
    /// <param name="textUtf8">The raw UTF-8 text bytes from the database column.</param>
    /// <param name="queryTermsUtf8">Pre-encoded UTF-8 query terms.</param>
    /// <returns>
    /// A non-negative TF score. Higher = more relevant.
    /// Formula: sum(count(term_i in text)) / textLength.
    /// Returns 0 if text is empty or no terms match.
    /// </returns>
    internal static float Score(ReadOnlySpan<byte> textUtf8, byte[][] queryTermsUtf8)
    {
        if (textUtf8.IsEmpty || queryTermsUtf8.Length == 0)
            return 0f;

        int totalHits = 0;
        for (int t = 0; t < queryTermsUtf8.Length; t++)
        {
            totalHits += CountOccurrences(textUtf8, queryTermsUtf8[t]);
        }

        if (totalHits == 0)
            return 0f;

        // Normalize by text length (in bytes) to avoid bias toward longer documents
        return (float)totalHits / textUtf8.Length;
    }

    /// <summary>
    /// Tokenizes a query string into whitespace-separated terms and encodes each as UTF-8.
    /// </summary>
    /// <param name="queryText">The raw query string (e.g., "neural networks").</param>
    /// <returns>Array of UTF-8-encoded term byte arrays.</returns>
    internal static byte[][] TokenizeQuery(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return [];

        var parts = queryText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var terms = new byte[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
            terms[i] = Encoding.UTF8.GetBytes(parts[i]);
        return terms;
    }

    /// <summary>
    /// Counts non-overlapping occurrences of a pattern in the text.
    /// Uses <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
    /// for SIMD-accelerated search.
    /// </summary>
    private static int CountOccurrences(ReadOnlySpan<byte> text, ReadOnlySpan<byte> pattern)
    {
        if (pattern.IsEmpty) return 0;

        int count = 0;
        var remaining = text;
        while (remaining.Length >= pattern.Length)
        {
            int idx = remaining.IndexOf(pattern);
            if (idx < 0) break;
            count++;
            remaining = remaining[(idx + pattern.Length)..];
        }
        return count;
    }
}
