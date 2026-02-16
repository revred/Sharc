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

using Sharc.Graph.Schema;

namespace Sharc.Graph.Tests.Unit.Schema;

[TestClass]
public class NativeSchemaAdapterTests
{
    [TestMethod]
    public void NativeAdapter_ExpectedConfiguration()
    {
        var adapter = new NativeSchemaAdapter();
        
        Assert.AreEqual("_concepts", adapter.NodeTableName);
        Assert.AreEqual("_relations", adapter.EdgeTableName);
        
        Assert.AreEqual("id", adapter.NodeIdColumn);
        Assert.AreEqual("key", adapter.NodeKeyColumn);
        Assert.AreEqual("kind", adapter.NodeTypeColumn);
        Assert.AreEqual("data", adapter.NodeDataColumn);
        
        Assert.AreEqual("source_key", adapter.EdgeOriginColumn);
        Assert.AreEqual("target_key", adapter.EdgeTargetColumn);
        Assert.AreEqual("kind", adapter.EdgeKindColumn);
    }
}
