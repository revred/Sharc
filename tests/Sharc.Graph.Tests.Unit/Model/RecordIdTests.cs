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
public class RecordIdTests
{
    [TestMethod]
    public void Parse_ValidFormat_ReturnsRecordId()
    {
        var id = RecordId.Parse("person:alice");
        Assert.AreEqual("person", id.Table);
        Assert.AreEqual("alice", id.Id);
    }

    [TestMethod]
    public void Parse_TableAndId_Extracted()
    {
        var id = RecordId.Parse("file:src/Auth.cs");
        Assert.AreEqual("file", id.Table);
        Assert.AreEqual("src/Auth.cs", id.Id);
    }

    [TestMethod]
    public void Parse_NoColon_ThrowsFormatException()
    {
        try
        {
            RecordId.Parse("invalid");
            Assert.Fail("Expected FormatException was not thrown.");
        }
        catch (FormatException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void Parse_MultipleColons_TakesFirstAsDelimiter()
    {
        var id = RecordId.Parse("log:2024:01:01");
        Assert.AreEqual("log", id.Table);
        Assert.AreEqual("2024:01:01", id.Id);
    }

    [TestMethod]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.IsFalse(RecordId.TryParse(null, out _));
        Assert.IsFalse(RecordId.TryParse("", out _));
    }

    [TestMethod]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.IsTrue(RecordId.TryParse("a:b", out var result));
        Assert.AreEqual("a", result.Table);
        Assert.AreEqual("b", result.Id);
    }

    [TestMethod]
    public void IntegerConstruction_SetsKey()
    {
        var key = new NodeKey(123);
        var id = new RecordId(1, "guid", key);
        
        Assert.AreEqual("1", id.Table);
        Assert.AreEqual("guid", id.Id);
        Assert.AreEqual(key, id.Key);
        Assert.IsTrue(id.HasIntegerKey);
    }

    [TestMethod]
    public void HasIntegerKey_WhenKeySet_ReturnsTrue()
    {
        var id = new RecordId("t", "i", new NodeKey(1));
        Assert.IsTrue(id.HasIntegerKey);
    }

    [TestMethod]
    public void HasIntegerKey_WhenDefault_ReturnsFalse()
    {
        var id = new RecordId("t", "i");
        Assert.IsFalse(id.HasIntegerKey);
    }

    [TestMethod]
    public void IsRecordLink_ValidLink_ReturnsTrue()
    {
        Assert.IsTrue(RecordId.IsRecordLink("table:id"));
    }

    [TestMethod]
    public void IsRecordLink_NoColon_ReturnsFalse()
    {
        Assert.IsFalse(RecordId.IsRecordLink("tableid"));
    }
    
    [TestMethod]
    public void IsRecordLink_Slash_ReturnsFalse()
    {
        Assert.IsFalse(RecordId.IsRecordLink("http://example.com"));
    }
    
    [TestMethod]
    public void IsRecordLink_Space_ReturnsFalse()
    {
        Assert.IsFalse(RecordId.IsRecordLink("table : id"));
    }

    [TestMethod]
    public void Equality_SameTableAndId_AreEqual()
    {
        var id1 = new RecordId("t", "i");
        var id2 = new RecordId("t", "i");
        Assert.AreEqual(id1, id2);
    }
}
