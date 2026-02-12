/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sharc.Graph.Model;

namespace Sharc.Graph.Tests.Unit.Model;

[TestClass]
public class ChangeEventTests
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var id = RecordId.Parse("t:i");
        NodeKey key = 100;
        var before = new GraphRecord(id, key, 10, "{}") { CVN = 200 };
        var after = new GraphRecord(id, key, 10, "{\"key\":\"val\"}") { CVN = 201 };
        
        var evt = new ChangeEvent(ChangeType.Update, id, before, after); // Pass by value if struct, so implicit constructor match
        
        Assert.AreEqual(ChangeType.Update, evt.Type);
        Assert.AreEqual(id, evt.Id);
        Assert.AreEqual(before, evt.Before);
        Assert.AreEqual(after, evt.After);
    }
}
