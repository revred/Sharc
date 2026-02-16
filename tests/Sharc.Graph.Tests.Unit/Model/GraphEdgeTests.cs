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

namespace Sharc.Graph.Tests.Unit.Model;

[TestClass]
public class GraphEdgeTests
{
    [TestMethod]
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

        Assert.AreEqual(id, edge.Id);
        Assert.AreEqual(origin, edge.OriginKey);
        Assert.AreEqual(target, edge.TargetKey);
        Assert.AreEqual(1008, edge.Kind);
        Assert.AreEqual("has_op", edge.KindName);
        Assert.AreEqual("{}", edge.JsonData);
        Assert.AreEqual(now, edge.CreatedAt);
        Assert.AreEqual(10, edge.CVN);
        Assert.AreEqual(20, edge.LVN);
    }
}
