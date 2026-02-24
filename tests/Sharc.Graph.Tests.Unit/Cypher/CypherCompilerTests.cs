// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Cypher;
using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Cypher;

public sealed class CypherCompilerTests
{
    [Fact]
    public void Compile_SimpleMatch_MaxDepthOne()
    {
        var parser = new CypherParser("MATCH (a) |> [r:CALLS] |> (b) WHERE a.key = 42 RETURN b");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.Equal(1, plan.Policy.MaxDepth);
        Assert.Equal(new NodeKey(42), plan.StartKey);
        Assert.False(plan.IsShortestPath);
    }

    [Fact]
    public void Compile_VariableLength_MaxDepthN()
    {
        var parser = new CypherParser("MATCH (a) |> [r*..5] |> (b) WHERE a.key = 1 RETURN b");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.Equal(5, plan.Policy.MaxDepth);
    }

    [Fact]
    public void Compile_WhereKey_SetsStartKey()
    {
        var parser = new CypherParser("MATCH (x) |> [r] |> (y) WHERE x.key = 999 RETURN y");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.Equal(new NodeKey(999), plan.StartKey);
    }

    [Fact]
    public void Compile_ShortestPath_SetsBothKeys()
    {
        var parser = new CypherParser("MATCH p = shortestPath((a) |> [*] |> (b)) WHERE a.key = 1 AND b.key = 99 RETURN p");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.True(plan.IsShortestPath);
        Assert.Equal(new NodeKey(1), plan.StartKey);
        Assert.Equal(new NodeKey(99), plan.EndKey);
    }

    [Fact]
    public void Compile_KindFilter_SetsTraversalKind()
    {
        var parser = new CypherParser("MATCH (a) |> [r:CALLS] |> (b) WHERE a.key = 1 RETURN b");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.Equal(RelationKind.Calls, plan.Policy.Kind);
    }

    [Fact]
    public void Compile_IncomingDirection_SetsIncoming()
    {
        var parser = new CypherParser("MATCH (a) <| [r:CALLS] <| (b) WHERE a.key = 1 RETURN b");
        var stmt = parser.Parse();
        var plan = CypherCompiler.Compile(stmt);

        Assert.Equal(TraversalDirection.Incoming, plan.Policy.Direction);
    }
}
