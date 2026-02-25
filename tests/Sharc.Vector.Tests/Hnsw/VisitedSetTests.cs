// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class VisitedSetTests
{
    [Fact]
    public void IsVisited_Unvisited_ReturnsFalse()
    {
        using var set = new VisitedSet(100);

        Assert.False(set.IsVisited(0));
        Assert.False(set.IsVisited(50));
        Assert.False(set.IsVisited(99));
    }

    [Fact]
    public void Visit_ThenIsVisited_ReturnsTrue()
    {
        var set = new VisitedSet(100);

        set.Visit(42);

        Assert.True(set.IsVisited(42));
        Assert.False(set.IsVisited(41));
        Assert.False(set.IsVisited(43));

        set.Dispose();
    }

    [Fact]
    public void Visit_MultipleBitsInSameWord_AllReportVisited()
    {
        var set = new VisitedSet(100);

        set.Visit(0);
        set.Visit(1);
        set.Visit(31);

        Assert.True(set.IsVisited(0));
        Assert.True(set.IsVisited(1));
        Assert.True(set.IsVisited(31));
        Assert.False(set.IsVisited(2));

        set.Dispose();
    }

    [Fact]
    public void Visit_AcrossMultipleWords_AllReportVisited()
    {
        var set = new VisitedSet(200);

        set.Visit(0);
        set.Visit(32);
        set.Visit(64);
        set.Visit(199);

        Assert.True(set.IsVisited(0));
        Assert.True(set.IsVisited(32));
        Assert.True(set.IsVisited(64));
        Assert.True(set.IsVisited(199));

        set.Dispose();
    }

    [Fact]
    public void Reset_ClearsDirtyWords()
    {
        var set = new VisitedSet(100);

        set.Visit(10);
        set.Visit(50);
        set.Visit(99);

        Assert.True(set.IsVisited(10));
        Assert.True(set.IsVisited(50));
        Assert.True(set.IsVisited(99));

        set.Reset();

        Assert.False(set.IsVisited(10));
        Assert.False(set.IsVisited(50));
        Assert.False(set.IsVisited(99));

        set.Dispose();
    }

    [Fact]
    public void Reset_AllowsRevisiting()
    {
        var set = new VisitedSet(100);

        set.Visit(42);
        set.Reset();
        Assert.False(set.IsVisited(42));

        set.Visit(42);
        Assert.True(set.IsVisited(42));

        set.Dispose();
    }

    [Fact]
    public void Visit_BoundaryIndex_LastNode()
    {
        var set = new VisitedSet(33); // 2 words: [0..31] and [32]

        set.Visit(32);

        Assert.True(set.IsVisited(32));
        Assert.False(set.IsVisited(31));

        set.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var set = new VisitedSet(100);
        set.Dispose();
        set.Dispose(); // should not throw
    }
}
