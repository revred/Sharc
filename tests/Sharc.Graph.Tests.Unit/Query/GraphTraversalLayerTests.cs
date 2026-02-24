// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Sharc.Graph.Query;
using Sharc.Views;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Query;

public sealed class GraphTraversalLayerTests
{
    [Fact]
    public void Layer_Name_ReturnsConfiguredName()
    {
        var layer = new GraphTraversalLayer("reachable", null!, new NodeKey(1), default);
        Assert.Equal("reachable", layer.Name);
    }

    [Fact]
    public void Layer_Strategy_IsEager()
    {
        var layer = new GraphTraversalLayer("test", null!, new NodeKey(1), default);
        Assert.Equal(MaterializationStrategy.Eager, layer.Strategy);
    }

    [Fact]
    public void Layer_NullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GraphTraversalLayer(null!, null!, new NodeKey(1), default));
    }
}
