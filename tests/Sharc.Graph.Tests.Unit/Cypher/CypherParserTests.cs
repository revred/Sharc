// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Cypher;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Cypher;

public sealed class CypherParserTests
{
    [Fact]
    public void Parse_SimpleMatch_ReturnsStartAndEndNodes()
    {
        var parser = new CypherParser("MATCH (a) |> [r] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal("a", stmt.StartNode.Variable);
        Assert.Equal("b", stmt.EndNode!.Variable);
        Assert.NotNull(stmt.Relationship);
        Assert.Equal(CypherDirection.Outgoing, stmt.Relationship!.Direction);
    }

    [Fact]
    public void Parse_MatchWithKind_SetsRelationKind()
    {
        var parser = new CypherParser("MATCH (a) |> [r:CALLS] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal(15, stmt.Relationship!.Kind); // CALLS = 15
    }

    [Fact]
    public void Parse_VariableLength_SetsMaxHops()
    {
        var parser = new CypherParser("MATCH (a) |> [r:CALLS*..3] |> (b) WHERE a.key = 42 RETURN b");
        var stmt = parser.Parse();

        Assert.True(stmt.Relationship!.IsVariableLength);
        Assert.Equal(3, stmt.Relationship.MaxHops);
        Assert.Single(stmt.WhereConstraints);
        Assert.Equal(42L, stmt.WhereConstraints[0].Value);
    }

    [Fact]
    public void Parse_MatchWithWhere_ExtractsConstraint()
    {
        var parser = new CypherParser("MATCH (a) |> [r] |> (b) WHERE a.key = 100 RETURN b");
        var stmt = parser.Parse();

        Assert.Single(stmt.WhereConstraints);
        Assert.Equal("a", stmt.WhereConstraints[0].Variable);
        Assert.Equal("key", stmt.WhereConstraints[0].Property);
        Assert.Equal(100L, stmt.WhereConstraints[0].Value);
    }

    [Fact]
    public void Parse_ReturnVariable_CapturesReturnList()
    {
        var parser = new CypherParser("MATCH (a) |> [r] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Single(stmt.ReturnVariables);
        Assert.Equal("b", stmt.ReturnVariables[0]);
    }

    [Fact]
    public void Parse_ShortestPath_SetsFlag()
    {
        var parser = new CypherParser("MATCH p = shortestPath((a) |> [*] |> (b)) WHERE a.key = 1 AND b.key = 99 RETURN p");
        var stmt = parser.Parse();

        Assert.True(stmt.IsShortestPath);
        Assert.Equal("p", stmt.PathVariable);
        Assert.Equal(2, stmt.WhereConstraints.Count);
    }

    [Fact]
    public void Parse_IncomingDirection_SetsIncoming()
    {
        var parser = new CypherParser("MATCH (a) <| [r] <| (b) RETURN a");
        var stmt = parser.Parse();

        Assert.Equal(CypherDirection.Incoming, stmt.Relationship!.Direction);
    }

    [Fact]
    public void Parse_BidirectionalEdge_SetsBoth()
    {
        var parser = new CypherParser("MATCH (a) <|> [r] <|> (b) RETURN a");
        var stmt = parser.Parse();

        Assert.Equal(CypherDirection.Both, stmt.Relationship!.Direction);
    }

    [Fact]
    public void Parse_InvalidSyntax_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
        {
            var parser = new CypherParser("SELECT * FROM table");
            parser.Parse();
        });
    }
}
