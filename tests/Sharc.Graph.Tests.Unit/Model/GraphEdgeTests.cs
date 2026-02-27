// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Model;

public class GraphEdgeTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var origin = new NodeKey(1);
        var target = new NodeKey(2);
        var now = DateTimeOffset.UtcNow;
        
        var id = new RecordId("edges", "guid");
        var edge = new GraphEdge(id, origin, target, 1008)
        {
            KindName = "has_op",
            JsonData = "{}",
            CreatedAt = now,
            CVN = 10,
            LVN = 20
        };

        Assert.Equal(id, edge.Id);
        Assert.Equal(origin, edge.OriginKey);
        Assert.Equal(target, edge.TargetKey);
        Assert.Equal(1008, edge.Kind);
        Assert.Equal("has_op", edge.KindName);
        Assert.Equal("{}", edge.JsonData);
        Assert.Equal(now, edge.CreatedAt);
        Assert.Equal(10, edge.CVN);
        Assert.Equal(20, edge.LVN);
    }
}