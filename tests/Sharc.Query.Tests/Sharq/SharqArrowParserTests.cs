// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Sharc.Query.Sharq.Ast;
using Xunit;

namespace Sharc.Query.Tests.Sharq;

/// <summary>
/// Tests for Sharq edge/graph traversal parsing using |> (forward), <| (backward), <|> (bidi).
/// These are "shark tooth" operators — Sharc's native graph traversal syntax.
/// </summary>
public class SharqArrowParserTests
{
    private static SharqStar ParseExpr(string sql) => SharqParser.ParseExpression(sql);
    private static SelectStatement Parse(string sql) => SharqParser.Parse(sql);

    [Fact]
    public void Parse_ForwardEdge_SingleHop_ReturnsArrowStar()
    {
        // |>knows|>person
        var expr = ParseExpr("|>knows|>person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Null(arrow.Source);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal(ArrowDirection.Forward, arrow.Steps[0].Direction);
        Assert.Equal("knows", arrow.Steps[0].Identifier);
        Assert.Equal(ArrowDirection.Forward, arrow.Steps[1].Direction);
        Assert.Equal("person", arrow.Steps[1].Identifier);
    }

    [Fact]
    public void Parse_BackwardEdge_SingleHop_ReturnsArrowStar()
    {
        // <|placed<|person
        var expr = ParseExpr("<|placed<|person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Null(arrow.Source);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[0].Direction);
        Assert.Equal("placed", arrow.Steps[0].Identifier);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[1].Direction);
        Assert.Equal("person", arrow.Steps[1].Identifier);
    }

    [Fact]
    public void Parse_BidirectionalEdge_ReturnsArrowStar()
    {
        // <|>friends_with<|>person
        var expr = ParseExpr("<|>friends_with<|>person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal(ArrowDirection.Bidirectional, arrow.Steps[0].Direction);
        Assert.Equal("friends_with", arrow.Steps[0].Identifier);
        Assert.Equal(ArrowDirection.Bidirectional, arrow.Steps[1].Direction);
        Assert.Equal("person", arrow.Steps[1].Identifier);
    }

    [Fact]
    public void Parse_MultiHopEdge_ReturnsChainedSteps()
    {
        // |>order|>product<|order<|person — forward then backward
        var expr = ParseExpr("|>order|>product<|order<|person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Equal(4, arrow.Steps.Count);
        Assert.Equal(ArrowDirection.Forward, arrow.Steps[0].Direction);
        Assert.Equal("order", arrow.Steps[0].Identifier);
        Assert.Equal(ArrowDirection.Forward, arrow.Steps[1].Direction);
        Assert.Equal("product", arrow.Steps[1].Identifier);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[2].Direction);
        Assert.Equal("order", arrow.Steps[2].Identifier);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[3].Direction);
        Assert.Equal("person", arrow.Steps[3].Identifier);
    }

    [Fact]
    public void Parse_EdgeWithFieldAccess_SetsFinalField()
    {
        // |>knows|>person.name
        var expr = ParseExpr("|>knows|>person.name");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal("name", arrow.FinalField);
        Assert.False(arrow.FinalWildcard);
    }

    [Fact]
    public void Parse_EdgeWithWildcard_SetsFinalWildcard()
    {
        // |>order|>product.*
        var expr = ParseExpr("|>order|>product.*");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.True(arrow.FinalWildcard);
        Assert.Null(arrow.FinalField);
    }

    [Fact]
    public void Parse_EdgeInSelectList_Works()
    {
        var stmt = Parse("SELECT |>order|>product.* FROM person:billy");
        Assert.Single(stmt.Columns);
        var arrow = Assert.IsType<ArrowStar>(stmt.Columns[0].Expression);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.True(arrow.FinalWildcard);
        Assert.Equal("person", stmt.From.Name);
        Assert.Equal("billy", stmt.From.RecordId);
    }

    [Fact]
    public void Parse_EdgeInWhereClause_Works()
    {
        // WHERE count(|>attests) > 3
        // Note: the arrow inside count() is an expression argument
        var stmt = Parse("SELECT * FROM agents WHERE count(|>attests) > 3");
        Assert.NotNull(stmt.Where);
        var gt = Assert.IsType<BinaryStar>(stmt.Where);
        Assert.Equal(BinaryOp.GreaterThan, gt.Op);
    }

    [Fact]
    public void Parse_RecordIdInFrom_ParsesTableAndId()
    {
        var stmt = Parse("SELECT * FROM person:alice");
        Assert.Equal("person", stmt.From.Name);
        Assert.Equal("alice", stmt.From.RecordId);
    }

    [Fact]
    public void Parse_RecordIdLiteral_ReturnsRecordIdStar()
    {
        var expr = ParseExpr("person:alice");
        var rid = Assert.IsType<RecordIdStar>(expr);
        Assert.Equal("person", rid.Table);
        Assert.Equal("alice", rid.Id);
    }

    [Fact]
    public void Parse_EdgeFromRecordId_CombinesSourceAndSteps()
    {
        // person:alice|>knows|>person
        var expr = ParseExpr("person:alice|>knows|>person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.NotNull(arrow.Source);
        var source = Assert.IsType<RecordIdStar>(arrow.Source);
        Assert.Equal("person", source.Table);
        Assert.Equal("alice", source.Id);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal("knows", arrow.Steps[0].Identifier);
        Assert.Equal("person", arrow.Steps[1].Identifier);
    }

    [Fact]
    public void Parse_EdgeFromIdentifier_CombinesSourceAndSteps()
    {
        // a|>knows|>person  (column 'a' as source)
        var expr = ParseExpr("a|>knows|>person");
        var arrow = Assert.IsType<ArrowStar>(expr);
        Assert.NotNull(arrow.Source);
        var source = Assert.IsType<ColumnRefStar>(arrow.Source);
        Assert.Equal("a", source.Name);
        Assert.Equal(2, arrow.Steps.Count);
    }

    [Fact]
    public void Parse_EdgeWithAliasInSelect_Works()
    {
        var stmt = Parse("SELECT |>order|>product.name AS product_name FROM person:billy");
        Assert.Single(stmt.Columns);
        Assert.Equal("product_name", stmt.Columns[0].Alias);
        Assert.IsType<ArrowStar>(stmt.Columns[0].Expression);
    }

    [Fact]
    public void Parse_BackEdgeQuery_FullStatement()
    {
        // Who bought this product?
        var stmt = Parse("SELECT <|order<|person.* FROM product:crystal_cave");
        var arrow = Assert.IsType<ArrowStar>(stmt.Columns[0].Expression);
        Assert.Equal(2, arrow.Steps.Count);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[0].Direction);
        Assert.Equal("order", arrow.Steps[0].Identifier);
        Assert.Equal(ArrowDirection.Backward, arrow.Steps[1].Direction);
        Assert.Equal("person", arrow.Steps[1].Identifier);
        Assert.True(arrow.FinalWildcard);
    }
}
