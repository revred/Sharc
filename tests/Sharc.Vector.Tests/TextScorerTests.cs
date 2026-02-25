// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class TextScorerTests
{
    // ── Score ────────────────────────────────────────────────

    [Fact]
    public void Score_SingleTermSingleOccurrence_ReturnsNormalized()
    {
        byte[] text = "hello world"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("hello")];

        float score = TextScorer.Score(text, terms);

        // 1 hit / 11 bytes
        Assert.Equal(1.0f / 11, score, precision: 5);
    }

    [Fact]
    public void Score_SingleTermMultipleOccurrences_CountsAll()
    {
        byte[] text = "cat and cat and cat"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("cat")];

        float score = TextScorer.Score(text, terms);

        // 3 hits / 19 bytes
        Assert.Equal(3.0f / 19, score, precision: 5);
    }

    [Fact]
    public void Score_MultipleTerms_SumsOccurrences()
    {
        byte[] text = "neural networks and deep neural"u8.ToArray();
        byte[][] terms =
        [
            Encoding.UTF8.GetBytes("neural"),
            Encoding.UTF8.GetBytes("deep")
        ];

        float score = TextScorer.Score(text, terms);

        // neural=2, deep=1, total=3 / 31 bytes
        Assert.Equal(3.0f / 31, score, precision: 5);
    }

    [Fact]
    public void Score_NoMatch_ReturnsZero()
    {
        byte[] text = "hello world"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("xyz")];

        float score = TextScorer.Score(text, terms);

        Assert.Equal(0f, score);
    }

    [Fact]
    public void Score_EmptyText_ReturnsZero()
    {
        byte[] text = Array.Empty<byte>();
        byte[][] terms = [Encoding.UTF8.GetBytes("hello")];

        float score = TextScorer.Score(text, terms);

        Assert.Equal(0f, score);
    }

    [Fact]
    public void Score_EmptyTerms_ReturnsZero()
    {
        byte[] text = "hello world"u8.ToArray();
        byte[][] terms = Array.Empty<byte[]>();

        float score = TextScorer.Score(text, terms);

        Assert.Equal(0f, score);
    }

    [Fact]
    public void Score_CaseSensitive_NoMatch()
    {
        byte[] text = "Hello"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("hello")];

        float score = TextScorer.Score(text, terms);

        Assert.Equal(0f, score);
    }

    [Fact]
    public void Score_OverlappingPatterns_NonOverlapping()
    {
        byte[] text = "aaa"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("aa")];

        float score = TextScorer.Score(text, terms);

        // Non-overlapping: "aa" found at offset 0, next search from offset 2, no room for another
        Assert.Equal(1.0f / 3, score, precision: 5);
    }

    [Fact]
    public void Score_PatternAtStartMiddleEnd_CountsAll()
    {
        byte[] text = "abcXXXabcYYYabc"u8.ToArray();
        byte[][] terms = [Encoding.UTF8.GetBytes("abc")];

        float score = TextScorer.Score(text, terms);

        // 3 hits / 15 bytes
        Assert.Equal(3.0f / 15, score, precision: 5);
    }

    // ── TokenizeQuery ───────────────────────────────────────

    [Fact]
    public void TokenizeQuery_SimplePhrase_SplitsOnWhitespace()
    {
        byte[][] terms = TextScorer.TokenizeQuery("neural networks");

        Assert.Equal(2, terms.Length);
        Assert.Equal("neural"u8.ToArray(), terms[0]);
        Assert.Equal("networks"u8.ToArray(), terms[1]);
    }

    [Fact]
    public void TokenizeQuery_MultipleSpaces_IgnoresEmpty()
    {
        byte[][] terms = TextScorer.TokenizeQuery("  neural   networks  ");

        Assert.Equal(2, terms.Length);
        Assert.Equal("neural"u8.ToArray(), terms[0]);
        Assert.Equal("networks"u8.ToArray(), terms[1]);
    }

    [Fact]
    public void TokenizeQuery_EmptyString_ReturnsEmpty()
    {
        byte[][] terms = TextScorer.TokenizeQuery("");

        Assert.Empty(terms);
    }

    [Fact]
    public void TokenizeQuery_SingleTerm_ReturnsSingle()
    {
        byte[][] terms = TextScorer.TokenizeQuery("neural");

        Assert.Single(terms);
        Assert.Equal("neural"u8.ToArray(), terms[0]);
    }
}
