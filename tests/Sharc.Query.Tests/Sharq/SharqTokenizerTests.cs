// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

public class SharqTokenizerTests
{
    private static SharqToken Tokenize(string sql)
    {
        var tokenizer = new SharqTokenizer(sql.AsSpan());
        return tokenizer.NextToken();
    }

    private static SharqToken[] TokenizeAll(string sql)
    {
        var tokenizer = new SharqTokenizer(sql.AsSpan());
        var tokens = new System.Collections.Generic.List<SharqToken>();
        while (true)
        {
            var token = tokenizer.NextToken();
            tokens.Add(token);
            if (token.Kind == SharqTokenKind.Eof) break;
        }
        return tokens.ToArray();
    }

    [Fact]
    public void NextToken_SelectKeyword_ReturnsSelect()
    {
        var token = Tokenize("SELECT");
        Assert.Equal(SharqTokenKind.Select, token.Kind);
    }

    [Fact]
    public void NextToken_Identifier_ReturnsIdentifierWithCorrectSpan()
    {
        var token = Tokenize("users");
        Assert.Equal(SharqTokenKind.Identifier, token.Kind);
        Assert.Equal(0, token.Start);
        Assert.Equal(5, token.Length);
    }

    [Fact]
    public void NextToken_QuotedIdentifier_HandlesDoubleQuotes()
    {
        var token = Tokenize("\"my table\"");
        Assert.Equal(SharqTokenKind.Identifier, token.Kind);
        // Start/Length should cover content inside quotes
        Assert.Equal(1, token.Start);
        Assert.Equal(8, token.Length);
    }

    [Fact]
    public void NextToken_QuotedIdentifier_HandlesBrackets()
    {
        var token = Tokenize("[my column]");
        Assert.Equal(SharqTokenKind.Identifier, token.Kind);
        Assert.Equal(1, token.Start);
        Assert.Equal(9, token.Length);
    }

    [Fact]
    public void NextToken_QuotedIdentifier_HandlesBackticks()
    {
        var token = Tokenize("`my_field`");
        Assert.Equal(SharqTokenKind.Identifier, token.Kind);
        Assert.Equal(1, token.Start);
        Assert.Equal(8, token.Length);
    }

    [Fact]
    public void NextToken_IntegerLiteral_ParsesInlineValue()
    {
        var token = Tokenize("42");
        Assert.Equal(SharqTokenKind.Integer, token.Kind);
        Assert.Equal(42L, token.IntegerValue);
    }

    [Fact]
    public void NextToken_NegativeInteger_ParsesAsInteger()
    {
        // Negative sign is a separate unary operator, not part of the literal
        var tokens = TokenizeAll("-42");
        Assert.Equal(SharqTokenKind.Minus, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Integer, tokens[1].Kind);
        Assert.Equal(42L, tokens[1].IntegerValue);
    }

    [Fact]
    public void NextToken_FloatLiteral_ParsesInlineValue()
    {
        var token = Tokenize("3.14");
        Assert.Equal(SharqTokenKind.Float, token.Kind);
        Assert.Equal(3.14, token.FloatValue, 5);
    }

    [Fact]
    public void NextToken_StringLiteral_HandlesSingleQuotes()
    {
        var token = Tokenize("'hello world'");
        Assert.Equal(SharqTokenKind.String, token.Kind);
        // Start/Length cover content inside quotes
        Assert.Equal(1, token.Start);
        Assert.Equal(11, token.Length);
    }

    [Fact]
    public void NextToken_StringLiteral_EscapedQuote()
    {
        // SQL-style escaping: '' becomes '
        var token = Tokenize("'it''s'");
        Assert.Equal(SharqTokenKind.String, token.Kind);
    }

    [Theory]
    [InlineData("=", (int)SharqTokenKind.Equal)]
    [InlineData("!=", (int)SharqTokenKind.NotEqual)]
    [InlineData("<>", (int)SharqTokenKind.NotEqual)]
    [InlineData("<", (int)SharqTokenKind.LessThan)]
    [InlineData(">", (int)SharqTokenKind.GreaterThan)]
    [InlineData("<=", (int)SharqTokenKind.LessOrEqual)]
    [InlineData(">=", (int)SharqTokenKind.GreaterOrEqual)]
    public void NextToken_ComparisonOperators_ReturnsCorrectKind(string input, int expected)
    {
        var token = Tokenize(input);
        Assert.Equal((SharqTokenKind)expected, token.Kind);
    }

    [Fact]
    public void NextToken_MatchAtAt_ReturnsMatchOperator()
    {
        var token = Tokenize("@@");
        Assert.Equal(SharqTokenKind.Match, token.Kind);
    }

    [Fact]
    public void NextToken_MatchAtAndAt_ReturnsMatchAndOperator()
    {
        var token = Tokenize("@AND@");
        Assert.Equal(SharqTokenKind.MatchAnd, token.Kind);
    }

    [Fact]
    public void NextToken_MatchAtOrAt_ReturnsMatchOrOperator()
    {
        var token = Tokenize("@OR@");
        Assert.Equal(SharqTokenKind.MatchOr, token.Kind);
    }

    [Theory]
    [InlineData(",", (int)SharqTokenKind.Comma)]
    [InlineData(".", (int)SharqTokenKind.Dot)]
    [InlineData("*", (int)SharqTokenKind.Star)]
    [InlineData("(", (int)SharqTokenKind.LeftParen)]
    [InlineData(")", (int)SharqTokenKind.RightParen)]
    [InlineData(";", (int)SharqTokenKind.Semicolon)]
    [InlineData("+", (int)SharqTokenKind.Plus)]
    [InlineData("-", (int)SharqTokenKind.Minus)]
    [InlineData("/", (int)SharqTokenKind.Slash)]
    [InlineData("%", (int)SharqTokenKind.Percent)]
    [InlineData(":", (int)SharqTokenKind.Colon)]
    public void NextToken_Punctuation_ReturnsCorrectKind(string input, int expected)
    {
        var token = Tokenize(input);
        Assert.Equal((SharqTokenKind)expected, token.Kind);
    }

    [Fact]
    public void NextToken_ForwardEdge_ReturnsEdgeToken()
    {
        var token = Tokenize("|>");
        Assert.Equal(SharqTokenKind.Edge, token.Kind);
    }

    [Fact]
    public void NextToken_BackEdge_ReturnsBackEdgeToken()
    {
        var token = Tokenize("<|");
        Assert.Equal(SharqTokenKind.BackEdge, token.Kind);
    }

    [Fact]
    public void NextToken_BidirectionalEdge_ReturnsBidiEdgeToken()
    {
        var token = Tokenize("<|>");
        Assert.Equal(SharqTokenKind.BidiEdge, token.Kind);
    }

    [Fact]
    public void NextToken_WhitespaceSkipped_ReturnsNextMeaningful()
    {
        var token = Tokenize("   SELECT");
        Assert.Equal(SharqTokenKind.Select, token.Kind);
        Assert.Equal(3, token.Start);
    }

    [Fact]
    public void NextToken_EndOfInput_ReturnsEof()
    {
        var token = Tokenize("");
        Assert.Equal(SharqTokenKind.Eof, token.Kind);
    }

    [Fact]
    public void NextToken_EndOfInput_AfterToken_ReturnsEof()
    {
        var tokens = TokenizeAll("42");
        Assert.Equal(SharqTokenKind.Integer, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Eof, tokens[1].Kind);
    }

    [Theory]
    [InlineData("SELECT", (int)SharqTokenKind.Select)]
    [InlineData("select", (int)SharqTokenKind.Select)]
    [InlineData("Select", (int)SharqTokenKind.Select)]
    [InlineData("FROM", (int)SharqTokenKind.From)]
    [InlineData("WHERE", (int)SharqTokenKind.Where)]
    [InlineData("GROUP", (int)SharqTokenKind.Group)]
    [InlineData("BY", (int)SharqTokenKind.By)]
    [InlineData("ORDER", (int)SharqTokenKind.Order)]
    [InlineData("ASC", (int)SharqTokenKind.Asc)]
    [InlineData("DESC", (int)SharqTokenKind.Desc)]
    [InlineData("LIMIT", (int)SharqTokenKind.Limit)]
    [InlineData("OFFSET", (int)SharqTokenKind.Offset)]
    [InlineData("AND", (int)SharqTokenKind.And)]
    [InlineData("OR", (int)SharqTokenKind.Or)]
    [InlineData("NOT", (int)SharqTokenKind.Not)]
    [InlineData("IN", (int)SharqTokenKind.In)]
    [InlineData("BETWEEN", (int)SharqTokenKind.Between)]
    [InlineData("LIKE", (int)SharqTokenKind.Like)]
    [InlineData("IS", (int)SharqTokenKind.Is)]
    [InlineData("NULL", (int)SharqTokenKind.Null)]
    [InlineData("TRUE", (int)SharqTokenKind.True)]
    [InlineData("FALSE", (int)SharqTokenKind.False)]
    [InlineData("AS", (int)SharqTokenKind.As)]
    [InlineData("DISTINCT", (int)SharqTokenKind.Distinct)]
    public void NextToken_AllKeywords_RecognizedCorrectly(string input, int expected)
    {
        var token = Tokenize(input);
        Assert.Equal((SharqTokenKind)expected, token.Kind);
    }

    [Fact]
    public void NextToken_MultipleTokens_SequentialParsing()
    {
        var tokens = TokenizeAll("SELECT * FROM users WHERE age > 30");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[3].Kind);
        Assert.Equal(SharqTokenKind.Where, tokens[4].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[5].Kind);
        Assert.Equal(SharqTokenKind.GreaterThan, tokens[6].Kind);
        Assert.Equal(SharqTokenKind.Integer, tokens[7].Kind);
        Assert.Equal(30L, tokens[7].IntegerValue);
        Assert.Equal(SharqTokenKind.Eof, tokens[8].Kind);
    }

    [Fact]
    public void NextToken_EdgeSequence_ParsesCorrectly()
    {
        var tokens = TokenizeAll("|>knows|>person");
        Assert.Equal(SharqTokenKind.Edge, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.Edge, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[3].Kind);
        Assert.Equal(SharqTokenKind.Eof, tokens[4].Kind);
    }

    [Fact]
    public void NextToken_BackEdgeSequence_ParsesCorrectly()
    {
        var tokens = TokenizeAll("<|placed<|person");
        Assert.Equal(SharqTokenKind.BackEdge, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.BackEdge, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[3].Kind);
    }

    [Fact]
    public void NextToken_RecordIdPattern_ParsesAsThreeTokens()
    {
        // person:alice is identifier + colon + identifier
        var tokens = TokenizeAll("person:alice");
        Assert.Equal(SharqTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Colon, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Eof, tokens[3].Kind);
    }

    [Fact]
    public void GetText_ReturnsCorrectSpan()
    {
        var sql = "SELECT users";
        var tokenizer = new SharqTokenizer(sql.AsSpan());
        var token = tokenizer.NextToken(); // SELECT
        var text = tokenizer.GetText(token);
        Assert.True(text.Equals("SELECT".AsSpan(), System.StringComparison.Ordinal));
    }

    // ─── Parameter $name ─────────────────────────────────────────────

    [Fact]
    public void NextToken_Parameter_ReturnsParameter()
    {
        var token = Tokenize("$limit");
        Assert.Equal(SharqTokenKind.Parameter, token.Kind);
    }

    [Fact]
    public void NextToken_Parameter_SpanExcludesDollar()
    {
        var sql = "$limit";
        var tokenizer = new SharqTokenizer(sql.AsSpan());
        var token = tokenizer.NextToken();
        Assert.Equal(SharqTokenKind.Parameter, token.Kind);
        var text = tokenizer.GetText(token);
        Assert.True(text.Equals("limit".AsSpan(), System.StringComparison.Ordinal));
    }

    [Fact]
    public void NextToken_Parameter_WithUnderscore_Works()
    {
        var sql = "$user_id";
        var tokenizer = new SharqTokenizer(sql.AsSpan());
        var token = tokenizer.NextToken();
        Assert.Equal(SharqTokenKind.Parameter, token.Kind);
        var text = tokenizer.GetText(token);
        Assert.True(text.Equals("user_id".AsSpan(), System.StringComparison.Ordinal));
    }

    [Fact]
    public void NextToken_MultipleParameters_ParseCorrectly()
    {
        var tokens = TokenizeAll("$a $b $c");
        Assert.Equal(SharqTokenKind.Parameter, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Parameter, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.Parameter, tokens[2].Kind);
    }

    // ─── New keywords: CASE WHEN THEN ELSE END HAVING CAST ──────────

    [Fact]
    public void NextToken_CaseKeyword_ReturnsCase()
    {
        Assert.Equal(SharqTokenKind.Case, Tokenize("CASE").Kind);
        Assert.Equal(SharqTokenKind.Case, Tokenize("case").Kind);
    }

    [Fact]
    public void NextToken_WhenKeyword_ReturnsWhen()
    {
        Assert.Equal(SharqTokenKind.When, Tokenize("WHEN").Kind);
        Assert.Equal(SharqTokenKind.When, Tokenize("when").Kind);
    }

    [Fact]
    public void NextToken_ThenKeyword_ReturnsThen()
    {
        Assert.Equal(SharqTokenKind.Then, Tokenize("THEN").Kind);
    }

    [Fact]
    public void NextToken_ElseKeyword_ReturnsElse()
    {
        Assert.Equal(SharqTokenKind.Else, Tokenize("ELSE").Kind);
    }

    [Fact]
    public void NextToken_EndKeyword_ReturnsEnd()
    {
        Assert.Equal(SharqTokenKind.End, Tokenize("END").Kind);
    }

    [Fact]
    public void NextToken_HavingKeyword_ReturnsHaving()
    {
        Assert.Equal(SharqTokenKind.Having, Tokenize("HAVING").Kind);
        Assert.Equal(SharqTokenKind.Having, Tokenize("having").Kind);
    }

    [Fact]
    public void NextToken_CastKeyword_ReturnsCast()
    {
        Assert.Equal(SharqTokenKind.Cast, Tokenize("CAST").Kind);
        Assert.Equal(SharqTokenKind.Cast, Tokenize("cast").Kind);
    }

    [Fact]
    public void NextToken_CaseWhenSequence_ParsesCorrectly()
    {
        var tokens = TokenizeAll("CASE WHEN x THEN y ELSE z END");
        Assert.Equal(SharqTokenKind.Case, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.When, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Then, tokens[3].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[4].Kind);
        Assert.Equal(SharqTokenKind.Else, tokens[5].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[6].Kind);
        Assert.Equal(SharqTokenKind.End, tokens[7].Kind);
    }

    // ─── New keywords: WITH OVER PARTITION NULLS UNION INTERSECT EXCEPT EXISTS

    [Fact]
    public void NextToken_NewKeywords_WithOverUnionExists()
    {
        Assert.Equal(SharqTokenKind.With, Tokenize("WITH").Kind);
        Assert.Equal(SharqTokenKind.Over, Tokenize("OVER").Kind);
        Assert.Equal(SharqTokenKind.Partition, Tokenize("PARTITION").Kind);
        Assert.Equal(SharqTokenKind.Nulls, Tokenize("NULLS").Kind);
        Assert.Equal(SharqTokenKind.Union, Tokenize("UNION").Kind);
        Assert.Equal(SharqTokenKind.Intersect, Tokenize("INTERSECT").Kind);
        Assert.Equal(SharqTokenKind.Except, Tokenize("EXCEPT").Kind);
        Assert.Equal(SharqTokenKind.Exists, Tokenize("EXISTS").Kind);
    }

    [Fact]
    public void NextToken_NewKeywords_CaseInsensitive()
    {
        Assert.Equal(SharqTokenKind.With, Tokenize("with").Kind);
        Assert.Equal(SharqTokenKind.Over, Tokenize("over").Kind);
        Assert.Equal(SharqTokenKind.Union, Tokenize("union").Kind);
        Assert.Equal(SharqTokenKind.Exists, Tokenize("exists").Kind);
        Assert.Equal(SharqTokenKind.Partition, Tokenize("partition").Kind);
    }

    // ─── SQL Comments ─────────────────────────────────────────────────

    [Fact]
    public void NextToken_LineComment_SkippedBetweenTokens()
    {
        var tokens = TokenizeAll("SELECT -- this is a comment\n* FROM t");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[3].Kind);
    }

    [Fact]
    public void NextToken_LineCommentAtEnd_SkippedCompletely()
    {
        var tokens = TokenizeAll("42 -- trailing comment");
        Assert.Equal(SharqTokenKind.Integer, tokens[0].Kind);
        Assert.Equal(42L, tokens[0].IntegerValue);
        Assert.Equal(SharqTokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void NextToken_BlockComment_SkippedBetweenTokens()
    {
        var tokens = TokenizeAll("SELECT /* comment */ * FROM t");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[3].Kind);
    }

    [Fact]
    public void NextToken_BlockCommentMultiLine_Skipped()
    {
        var tokens = TokenizeAll("SELECT /* multi\nline\ncomment */ * FROM t");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[2].Kind);
    }

    [Fact]
    public void NextToken_EmptyBlockComment_Skipped()
    {
        var tokens = TokenizeAll("SELECT /**/ * FROM t");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[2].Kind);
    }

    [Fact]
    public void NextToken_UnterminatedBlockComment_ReachesEof()
    {
        var tokens = TokenizeAll("SELECT /* never closed");
        Assert.Equal(SharqTokenKind.Select, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Parse_QueryWithComments_FullStatement()
    {
        // End-to-end: comments should be transparent to the parser
        var stmt = SharqParser.Parse(
            "SELECT -- pick columns\n" +
            "  name, /* the age column */ age\n" +
            "FROM users -- main table\n" +
            "WHERE age > 18");
        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal("users", stmt.From.Name);
        Assert.NotNull(stmt.Where);
    }

    // ─── Pipe Shorthand Operators ─────────────────────────────────────

    [Fact]
    public void NextToken_PipeUnion_ReturnsUnionToken()
    {
        Assert.Equal(SharqTokenKind.Union, Tokenize("|u").Kind);
    }

    [Fact]
    public void NextToken_PipeUnionAll_ReturnsUnionAllToken()
    {
        Assert.Equal(SharqTokenKind.UnionAll, Tokenize("|a").Kind);
    }

    [Fact]
    public void NextToken_PipeIntersect_ReturnsIntersectToken()
    {
        Assert.Equal(SharqTokenKind.Intersect, Tokenize("|n").Kind);
    }

    [Fact]
    public void NextToken_PipeExcept_ReturnsExceptToken()
    {
        Assert.Equal(SharqTokenKind.Except, Tokenize("|x").Kind);
    }

    [Fact]
    public void NextToken_PipeExists_ReturnsExistsToken()
    {
        Assert.Equal(SharqTokenKind.Exists, Tokenize("|?").Kind);
    }

    [Fact]
    public void NextToken_PipeUnion_CorrectStartAndLength()
    {
        var token = Tokenize("|u");
        Assert.Equal(0, token.Start);
        Assert.Equal(2, token.Length);
    }

    [Fact]
    public void NextToken_PipeEdge_StillWorks()
    {
        Assert.Equal(SharqTokenKind.Edge, Tokenize("|>").Kind);
    }

    [Fact]
    public void NextToken_PipeStandalone_StillReturnsEof()
    {
        Assert.Equal(SharqTokenKind.Eof, Tokenize("|").Kind);
    }

    [Fact]
    public void NextToken_PipeShorthandSequence_ParsesCorrectly()
    {
        var tokens = TokenizeAll("|u SELECT * FROM t2");
        Assert.Equal(SharqTokenKind.Union, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.Select, tokens[1].Kind);
        Assert.Equal(SharqTokenKind.Star, tokens[2].Kind);
        Assert.Equal(SharqTokenKind.From, tokens[3].Kind);
        Assert.Equal(SharqTokenKind.Identifier, tokens[4].Kind);
        Assert.Equal(SharqTokenKind.Eof, tokens[5].Kind);
    }

    [Fact]
    public void NextToken_PipeExistsFollowedByParen_ParsesCorrectly()
    {
        var tokens = TokenizeAll("|? (");
        Assert.Equal(SharqTokenKind.Exists, tokens[0].Kind);
        Assert.Equal(SharqTokenKind.LeftParen, tokens[1].Kind);
    }
}
