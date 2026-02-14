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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sharc.Graph.Model;

namespace Sharc.Graph.Tests.Unit.Model;

[TestClass]
public class TraversalTypesTests
{
    [TestMethod]
    public void TraversalNode_StoresData()
    {
        var id = RecordId.Parse("t:i");
        var record = new GraphRecord(id, 1, 10, "{}") { CreatedAt = DateTimeOffset.UtcNow };
        var path = new List<NodeKey> { 1, 2, 3 };
        
        var node = new TraversalNode(record, 2, path);
        
        Assert.AreEqual(record, node.Record);
        Assert.AreEqual(2, node.Depth);
        Assert.HasCount(3, node.Path!);
        Assert.AreEqual(new NodeKey(1), node.Path![0]);
    }

    [TestMethod]
    public void GraphResult_StoresNodes()
    {
        var id = RecordId.Parse("t:i");
        var record = new GraphRecord(id, 1, 10, "{}") { CreatedAt = DateTimeOffset.UtcNow };
        var node = new TraversalNode(record, 0, new List<NodeKey>{1});
        var list = new List<TraversalNode> { node };
        
        var result = new GraphResult(list);
        
        Assert.HasCount(1, result.Nodes);
        Assert.AreEqual(node, result.Nodes[0]);
    }

    [TestMethod]
    public void TraversalPolicy_Defaults()
    {
        var options = new TraversalPolicy();
        
        Assert.IsNull(options.MaxFanOut);
        Assert.IsNull(options.TargetTypeFilter);
        Assert.AreEqual(0L, options.StopAtKey.Value);
        Assert.AreEqual(TraversalDirection.Outgoing, options.Direction);
    }

    [TestMethod]
    public void TraversalPolicy_InitProps()
    {
        var options = new TraversalPolicy
        {
            MaxFanOut = 50,
            TargetTypeFilter = 3,
            StopAtKey = new NodeKey(999),
            Direction = TraversalDirection.Incoming
        };
        
        Assert.AreEqual(50, options.MaxFanOut);
        Assert.AreEqual(3, options.TargetTypeFilter);
        Assert.AreEqual(999L, options.StopAtKey.Value);
        Assert.AreEqual(TraversalDirection.Incoming, options.Direction);
    }
    
    [TestMethod]
    public void ContextSummary_StoresData()
    {
        var guid = Guid.NewGuid();
        var records = new List<GraphRecord>();
        var summary = new ContextSummary(guid, "Hello", 100, records);
        
        Assert.AreEqual(guid, summary.RootId);
        Assert.AreEqual("Hello", summary.SummaryText);
        Assert.AreEqual(100, summary.TokenCount);
        Assert.AreSame(records, summary.IncludedRecords);
    }
    
    [TestMethod]
    public void PathResult_StoresData()
    {
        var records = new List<GraphRecord>();
        var path = new PathResult(records, 1.5f);
        
        Assert.AreSame(records, path.Path);
        Assert.AreEqual(1.5f, path.Weight);
    }
}
