// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Cypher;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Cypher;

public sealed class CypherTokenizerTests
{
    private static CypherToken NextToken(ref CypherTokenizer tokenizer) => tokenizer.Next();

    [Fact]
    public void Tokenizer_MatchKeyword_ReturnsMatch()
    {
        var tokenizer = new CypherTokenizer("MATCH");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Match, tok.Kind);
    }

    [Fact]
    public void Tokenizer_ReturnKeyword_ReturnsReturn()
    {
        var tokenizer = new CypherTokenizer("RETURN");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Return, tok.Kind);
    }

    [Fact]
    public void Tokenizer_Edge_ReturnsPipeRight()
    {
        var tokenizer = new CypherTokenizer("|>");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Edge, tok.Kind);
        Assert.Equal(2, tok.Length);
    }

    [Fact]
    public void Tokenizer_BackEdge_ReturnsPipeLeft()
    {
        var tokenizer = new CypherTokenizer("<|");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.BackEdge, tok.Kind);
        Assert.Equal(2, tok.Length);
    }

    [Fact]
    public void Tokenizer_BidiEdge_ReturnsPipeBoth()
    {
        var tokenizer = new CypherTokenizer("<|>");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.BidiEdge, tok.Kind);
        Assert.Equal(3, tok.Length);
    }

    [Fact]
    public void Tokenizer_NodePattern_ParsesParensAndIdentifier()
    {
        var tokenizer = new CypherTokenizer("(a)");
        Assert.Equal(CypherTokenKind.LParen, NextToken(ref tokenizer).Kind);
        var ident = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Identifier, ident.Kind);
        Assert.Equal("a", ident.GetText("(a)").ToString());
        Assert.Equal(CypherTokenKind.RParen, NextToken(ref tokenizer).Kind);
    }

    [Fact]
    public void Tokenizer_RelWithKind_ParsesBracketColonKind()
    {
        var tokenizer = new CypherTokenizer("[r:CALLS]");
        Assert.Equal(CypherTokenKind.LBracket, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // r
        Assert.Equal(CypherTokenKind.Colon, NextToken(ref tokenizer).Kind);
        var kind = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Identifier, kind.Kind);
        Assert.Equal("CALLS", kind.GetText("[r:CALLS]").ToString());
        Assert.Equal(CypherTokenKind.RBracket, NextToken(ref tokenizer).Kind);
    }

    [Fact]
    public void Tokenizer_VariableLengthStar_ParsesStarDotDotNumber()
    {
        var tokenizer = new CypherTokenizer("*..3");
        Assert.Equal(CypherTokenKind.Star, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.DotDot, NextToken(ref tokenizer).Kind);
        var num = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Integer, num.Kind);
        Assert.Equal(3L, num.IntegerValue);
    }

    [Fact]
    public void Tokenizer_IntegerLiteral_ReturnsValue()
    {
        var tokenizer = new CypherTokenizer("42");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Integer, tok.Kind);
        Assert.Equal(42L, tok.IntegerValue);
    }

    [Fact]
    public void Tokenizer_Eof_ReturnsEofToken()
    {
        var tokenizer = new CypherTokenizer("");
        var tok = NextToken(ref tokenizer);
        Assert.Equal(CypherTokenKind.Eof, tok.Kind);
    }

    [Fact]
    public void Tokenizer_FullQuery_TokenizesCorrectly()
    {
        var input = "MATCH (a) |> [r:CALLS] |> (b) RETURN b";
        var tokenizer = new CypherTokenizer(input);
        Assert.Equal(CypherTokenKind.Match, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.LParen, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // a
        Assert.Equal(CypherTokenKind.RParen, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Edge, NextToken(ref tokenizer).Kind); // |>
        Assert.Equal(CypherTokenKind.LBracket, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // r
        Assert.Equal(CypherTokenKind.Colon, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // CALLS
        Assert.Equal(CypherTokenKind.RBracket, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Edge, NextToken(ref tokenizer).Kind); // |>
        Assert.Equal(CypherTokenKind.LParen, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // b
        Assert.Equal(CypherTokenKind.RParen, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Return, NextToken(ref tokenizer).Kind);
        Assert.Equal(CypherTokenKind.Identifier, NextToken(ref tokenizer).Kind); // b
        Assert.Equal(CypherTokenKind.Eof, NextToken(ref tokenizer).Kind);
    }
}
