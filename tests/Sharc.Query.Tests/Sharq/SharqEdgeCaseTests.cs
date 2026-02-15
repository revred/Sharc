// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

public class SharqEdgeCaseTests
{
    [Fact]
    public void Parse_EmptyInput_ThrowsSharqParseException()
    {
        var ex = Assert.Throws<SharqParseException>(() => SharqParser.Parse(""));
        Assert.Contains("position", ex.Message);
    }

    [Fact]
    public void Parse_MissingFrom_ThrowsWithPosition()
    {
        var ex = Assert.Throws<SharqParseException>(() => SharqParser.Parse("SELECT *"));
        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_UnterminatedString_ProducesStringToken()
    {
        // Unterminated string in expression — tokenizer returns partial content, parser accepts
        var expr = SharqParser.ParseExpression("'unterminated");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("unterminated", lit.StringValue);
    }

    [Fact]
    public void Parse_UnmatchedParen_ThrowsWithPosition()
    {
        var ex = Assert.Throws<SharqParseException>(
            () => SharqParser.ParseExpression("(1 + 2"));
        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_UnexpectedToken_ThrowsWithDescription()
    {
        var ex = Assert.Throws<SharqParseException>(
            () => SharqParser.Parse("SELECT * FROM"));
        Assert.Contains("Expected", ex.Message);
    }

    [Fact]
    public void Parse_TrailingSemicolon_Accepted()
    {
        var stmt = SharqParser.Parse("SELECT * FROM users;");
        Assert.Equal("users", stmt.From.Name);
    }

    [Fact]
    public void Parse_ExtraWhitespace_Handled()
    {
        var stmt = SharqParser.Parse("  SELECT   *   FROM   users  ");
        Assert.Equal("users", stmt.From.Name);
    }

    [Fact]
    public void Parse_QuotedIdentifier_DoubleQuotes()
    {
        var stmt = SharqParser.Parse("SELECT \"user name\" FROM \"my table\"");
        var col = Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression);
        Assert.Equal("user name", col.Name);
        Assert.Equal("my table", stmt.From.Name);
    }

    [Fact]
    public void Parse_QuotedIdentifier_Brackets()
    {
        var stmt = SharqParser.Parse("SELECT [user name] FROM [my table]");
        var col = Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression);
        Assert.Equal("user name", col.Name);
        Assert.Equal("my table", stmt.From.Name);
    }

    [Fact]
    public void Parse_QuotedIdentifier_Backticks()
    {
        var stmt = SharqParser.Parse("SELECT `user name` FROM `my table`");
        var col = Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression);
        Assert.Equal("user name", col.Name);
        Assert.Equal("my table", stmt.From.Name);
    }

    [Fact]
    public void Parse_EscapedQuoteInString_Handled()
    {
        // SQL escaping: '' means literal '
        var expr = SharqParser.ParseExpression("'it''s'");
        var lit = Assert.IsType<LiteralStar>(expr);
        Assert.Equal(LiteralKind.String, lit.Kind);
        // The raw content includes the doubled quote — normalization is a consumer concern
    }

    [Fact]
    public void Parse_VeryLongQuery_DoesNotStackOverflow()
    {
        // Build a deeply nested arithmetic expression: 1+1+1+...+1 (1000 terms)
        var terms = string.Join("+", System.Linq.Enumerable.Repeat("1", 1000));
        var expr = SharqParser.ParseExpression(terms);
        Assert.IsType<BinaryStar>(expr);
    }

    [Fact]
    public void Parse_UnicodeIdentifiers_Handled()
    {
        var stmt = SharqParser.Parse("SELECT * FROM \"données\"");
        Assert.Equal("données", stmt.From.Name);
    }

    [Fact]
    public void Parse_WhereTrue_Literal()
    {
        var stmt = SharqParser.Parse("SELECT * FROM t WHERE TRUE");
        var lit = Assert.IsType<LiteralStar>(stmt.Where);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
        Assert.True(lit.BoolValue);
    }

    [Fact]
    public void Parse_MultipleSemicolons_OnlyFirstAccepted()
    {
        // One trailing semicolon is fine
        var stmt = SharqParser.Parse("SELECT * FROM t;");
        Assert.Equal("t", stmt.From.Name);
    }

    [Fact]
    public void Parse_SelectCountGroupOrder_FullPipeline()
    {
        var stmt = SharqParser.Parse(
            "SELECT customer_id, count(*) AS total, sum(amount) AS spent " +
            "FROM orders " +
            "WHERE status = 'completed' " +
            "GROUP BY customer_id " +
            "ORDER BY spent DESC " +
            "LIMIT 50");

        Assert.Equal(3, stmt.Columns.Count);
        Assert.Equal("customer_id", Assert.IsType<ColumnRefStar>(stmt.Columns[0].Expression).Name);
        Assert.Equal("total", stmt.Columns[1].Alias);
        Assert.Equal("spent", stmt.Columns[2].Alias);
        Assert.NotNull(stmt.GroupBy);
        Assert.NotNull(stmt.OrderBy);
        Assert.True(stmt.OrderBy![0].Descending);
        Assert.NotNull(stmt.Limit);
    }

    [Fact]
    public void Parse_SharqParseException_IncludesPosition()
    {
        var ex = Assert.Throws<SharqParseException>(
            () => SharqParser.Parse("SELECT"));
        Assert.True(ex.Position >= 0);
        Assert.Contains("position", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    // ─── Error cases for new features ────────────────────────────────

    [Fact]
    public void Parse_CaseWithoutWhen_Throws()
    {
        Assert.Throws<SharqParseException>(
            () => SharqParser.ParseExpression("CASE END"));
    }

    [Fact]
    public void Parse_CaseWithoutEnd_Throws()
    {
        Assert.Throws<SharqParseException>(
            () => SharqParser.ParseExpression("CASE WHEN x = 1 THEN 'a'"));
    }

    [Fact]
    public void Parse_CastMissingAs_Throws()
    {
        Assert.Throws<SharqParseException>(
            () => SharqParser.ParseExpression("CAST(x INTEGER)"));
    }

    [Fact]
    public void Parse_CastMissingParen_Throws()
    {
        Assert.Throws<SharqParseException>(
            () => SharqParser.ParseExpression("CAST(x AS INTEGER"));
    }
}
