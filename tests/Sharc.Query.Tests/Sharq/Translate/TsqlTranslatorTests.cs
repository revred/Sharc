// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Sharc.Query.Sharq.Translate;
using Xunit;

namespace Sharc.Query.Tests.Sharq.Translate;

public class TsqlTranslatorTests
{
    // ─── Fast path ───────────────────────────────────────────────────

    [Fact]
    public void Translate_NoSpecialChars_ReturnsUnchanged()
    {
        const string sql = "SELECT * FROM users WHERE age > 18";
        var result = Tsql.Translate(sql);
        Assert.Same(sql, result); // same reference — zero allocation
    }

    // ─── @param → $param ─────────────────────────────────────────────

    [Fact]
    public void Translate_AtParam_ConvertedToDollar()
    {
        var result = Tsql.Translate("SELECT * FROM users WHERE id = @userId");
        Assert.Equal("SELECT * FROM users WHERE id = $userId", result);
    }

    [Fact]
    public void Translate_MultipleAtParams_AllConverted()
    {
        var result = Tsql.Translate("SELECT * FROM t WHERE a = @x AND b = @y");
        Assert.Equal("SELECT * FROM t WHERE a = $x AND b = $y", result);
    }

    [Fact]
    public void Translate_AtParamInString_Preserved()
    {
        var result = Tsql.Translate("SELECT * FROM t WHERE name = '@notaparam'");
        Assert.Equal("SELECT * FROM t WHERE name = '@notaparam'", result);
    }

    [Fact]
    public void Translate_AtParamWithUnderscore_Converted()
    {
        var result = Tsql.Translate("SELECT * FROM t WHERE col = @user_id");
        Assert.Equal("SELECT * FROM t WHERE col = $user_id", result);
    }

    // ─── N'string' → 'string' ────────────────────────────────────────

    [Fact]
    public void Translate_NPrefix_Stripped()
    {
        var result = Tsql.Translate("SELECT * FROM t WHERE name = N'hello'");
        Assert.Equal("SELECT * FROM t WHERE name = 'hello'", result);
    }

    [Fact]
    public void Translate_NPrefixLowercase_Stripped()
    {
        var result = Tsql.Translate("SELECT * FROM t WHERE name = n'hello'");
        Assert.Equal("SELECT * FROM t WHERE name = 'hello'", result);
    }

    // ─── SELECT TOP → LIMIT ──────────────────────────────────────────

    [Fact]
    public void Translate_SelectTop_ConvertedToLimit()
    {
        var result = Tsql.Translate("SELECT TOP 10 * FROM users");
        Assert.Equal("SELECT * FROM users LIMIT 10", result);
    }

    [Fact]
    public void Translate_SelectTopParens_ConvertedToLimit()
    {
        var result = Tsql.Translate("SELECT TOP (10) * FROM users");
        Assert.Equal("SELECT * FROM users LIMIT 10", result);
    }

    [Fact]
    public void Translate_SelectDistinctTop_ConvertedToLimit()
    {
        var result = Tsql.Translate("SELECT DISTINCT TOP 5 name FROM users");
        Assert.Equal("SELECT DISTINCT name FROM users LIMIT 5", result);
    }

    [Fact]
    public void Translate_TopWithSemicolon_LimitBeforeSemicolon()
    {
        var result = Tsql.Translate("SELECT TOP 10 * FROM users;");
        Assert.Equal("SELECT * FROM users LIMIT 10;", result);
    }

    [Fact]
    public void Translate_TopWithAtParam_Converted()
    {
        var result = Tsql.Translate("SELECT TOP (@maxRows) * FROM users");
        Assert.Equal("SELECT * FROM users LIMIT $maxRows", result);
    }

    // ─── Bracket identifiers ─────────────────────────────────────────

    [Fact]
    public void Translate_BracketIdentifiers_Preserved()
    {
        var result = Tsql.Translate("SELECT [user name] FROM [my table]");
        Assert.Equal("SELECT [user name] FROM [my table]", result);
    }

    // ─── Mixed / integration ─────────────────────────────────────────

    [Fact]
    public void Translate_MixedConversions_AllApplied()
    {
        var result = Tsql.Translate(
            "SELECT DISTINCT TOP 20 [first_name], [last_name] " +
            "FROM [employees] " +
            "WHERE department = @dept AND status = N'active';");
        Assert.Equal(
            "SELECT DISTINCT [first_name], [last_name] " +
            "FROM [employees] " +
            "WHERE department = $dept AND status = 'active' LIMIT 20;",
            result);
    }

    [Fact]
    public void Translate_ResultParseable_EndToEnd()
    {
        var sharql = Tsql.Translate(
            "SELECT TOP 5 name, age FROM users WHERE age > @minAge");
        var stmt = SharqParser.Parse(sharql);

        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal("users", stmt.From.Name);
        Assert.NotNull(stmt.Where);
        Assert.NotNull(stmt.Limit);

        var limit = Assert.IsType<LiteralStar>(stmt.Limit);
        Assert.Equal(5L, limit.IntegerValue);

        var where = Assert.IsType<BinaryStar>(stmt.Where);
        var param = Assert.IsType<ParameterStar>(where.Right);
        Assert.Equal("minAge", param.Name);
    }

    // ─── Span overload ───────────────────────────────────────────────

    [Fact]
    public void Translate_SpanOverload_WritesCorrectly()
    {
        var input = "SELECT * FROM t WHERE x = @val".AsSpan();
        Span<char> output = stackalloc char[256];
        int written = Tsql.Translate(input, output);

        var result = new string(output[..written]);
        Assert.Equal("SELECT * FROM t WHERE x = $val", result);
    }

    // ─── OFFSET FETCH → LIMIT OFFSET ────────────────────────────────

    [Fact]
    public void Translate_OffsetFetch_ConvertedToLimitOffset()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET 10 ROWS FETCH NEXT 20 ROWS ONLY");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT 20 OFFSET 10", result);
    }

    [Fact]
    public void Translate_OffsetFetchSingularRow_ConvertedToLimitOffset()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET 0 ROW FETCH NEXT 1 ROW ONLY");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT 1 OFFSET 0", result);
    }

    [Fact]
    public void Translate_OffsetFetchFirst_ConvertedToLimitOffset()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET 5 ROWS FETCH FIRST 10 ROWS ONLY");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT 10 OFFSET 5", result);
    }

    [Fact]
    public void Translate_OffsetFetchWithParams_ConvertedWithDollar()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT $take OFFSET $skip", result);
    }

    [Fact]
    public void Translate_OffsetFetchWithSemicolon_LimitBeforeSemicolon()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET 10 ROWS FETCH NEXT 20 ROWS ONLY;");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT 20 OFFSET 10;", result);
    }

    [Fact]
    public void Translate_OffsetFetchZero_Works()
    {
        var result = Tsql.Translate(
            "SELECT * FROM t ORDER BY x OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
        Assert.Equal(
            "SELECT * FROM t ORDER BY x LIMIT 10 OFFSET 0", result);
    }

    [Fact]
    public void Translate_OffsetWithoutFetch_PassedThrough()
    {
        // Sharq-native OFFSET (after LIMIT) should pass through unchanged
        const string sql = "SELECT * FROM t LIMIT 10 OFFSET 20";
        var result = Tsql.Translate(sql);
        Assert.Same(sql, result);
    }

    // ─── WITH (NOLOCK) → strip ──────────────────────────────────────

    [Fact]
    public void Translate_WithNolock_Stripped()
    {
        var result = Tsql.Translate(
            "SELECT * FROM users WITH (NOLOCK) WHERE id = 1");
        Assert.Equal(
            "SELECT * FROM users WHERE id = 1", result);
    }

    [Fact]
    public void Translate_WithNolockMultipleHints_Stripped()
    {
        var result = Tsql.Translate(
            "SELECT * FROM users WITH (NOLOCK, READPAST) WHERE id = 1");
        Assert.Equal(
            "SELECT * FROM users WHERE id = 1", result);
    }

    [Fact]
    public void Translate_WithNolockCaseInsensitive_Stripped()
    {
        var result = Tsql.Translate(
            "SELECT * FROM users with (nolock) WHERE id = 1");
        Assert.Equal(
            "SELECT * FROM users WHERE id = 1", result);
    }

    [Fact]
    public void Translate_WithAsCteSyntax_Preserved()
    {
        var result = Tsql.Translate(
            "WITH cte AS (SELECT * FROM t) SELECT * FROM cte");
        Assert.Equal(
            "WITH cte AS (SELECT * FROM t) SELECT * FROM cte", result);
    }

    [Fact]
    public void Translate_OffsetFetchAndNolock_BothConverted()
    {
        var result = Tsql.Translate(
            "SELECT * FROM users WITH (NOLOCK) ORDER BY id OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
        Assert.Equal(
            "SELECT * FROM users ORDER BY id LIMIT 10 OFFSET 0", result);
    }
}
