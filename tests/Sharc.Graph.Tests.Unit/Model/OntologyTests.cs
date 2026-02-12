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
public class OntologyTests
{
    [TestMethod]
    public void ConceptKind_ValuesAreDefined()
    {
        Assert.IsTrue(Enum.IsDefined(ConceptKind.File));
        Assert.IsTrue(Enum.IsDefined(ConceptKind.Class));
    }

    [TestMethod]
    public void RelationKind_ValuesAreDefined()
    {
        Assert.IsTrue(Enum.IsDefined(RelationKind.Contains));
        Assert.IsTrue(Enum.IsDefined(RelationKind.Calls));
    }
}
