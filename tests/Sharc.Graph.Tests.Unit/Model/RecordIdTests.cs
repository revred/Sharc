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

public class RecordIdTests
{
    [Fact]
    public void Parse_ValidFormat_ReturnsRecordId()
    {
        var id = RecordId.Parse("person:alice");
        Assert.Equal("person", id.Table);
        Assert.Equal("alice", id.Id);
    }

    [Fact]
    public void Parse_TableAndId_Extracted()
    {
        var id = RecordId.Parse("file:src/Auth.cs");
        Assert.Equal("file", id.Table);
        Assert.Equal("src/Auth.cs", id.Id);
    }

    [Fact]
    public void Parse_NoColon_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => RecordId.Parse("invalid"));
    }

    [Fact]
    public void Parse_MultipleColons_TakesFirstAsDelimiter()
    {
        var id = RecordId.Parse("log:2024:01:01");
        Assert.Equal("log", id.Table);
        Assert.Equal("2024:01:01", id.Id);
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.False(RecordId.TryParse(null, out _));
        Assert.False(RecordId.TryParse("", out _));
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(RecordId.TryParse("a:b", out var result));
        Assert.Equal("a", result.Table);
        Assert.Equal("b", result.Id);
    }

    [Fact]
    public void IntegerConstruction_SetsKey()
    {
        var key = new NodeKey(123);
        var id = new RecordId(1, "guid", key);
        
        Assert.Equal("1", id.Table);
        Assert.Equal("guid", id.Id);
        Assert.Equal(key, id.Key);
        Assert.True(id.HasIntegerKey);
    }

    [Fact]
    public void HasIntegerKey_WhenKeySet_ReturnsTrue()
    {
        var id = new RecordId("t", "i", new NodeKey(1));
        Assert.True(id.HasIntegerKey);
    }

    [Fact]
    public void HasIntegerKey_WhenDefault_ReturnsFalse()
    {
        var id = new RecordId("t", "i");
        Assert.False(id.HasIntegerKey);
    }

    [Fact]
    public void IsRecordLink_ValidLink_ReturnsTrue()
    {
        Assert.True(RecordId.IsRecordLink("table:id"));
    }

    [Fact]
    public void IsRecordLink_NoColon_ReturnsFalse()
    {
        Assert.False(RecordId.IsRecordLink("tableid"));
    }
    
    [Fact]
    public void IsRecordLink_Slash_ReturnsFalse()
    {
        Assert.False(RecordId.IsRecordLink("http://example.com"));
    }
    
    [Fact]
    public void IsRecordLink_Space_ReturnsFalse()
    {
        Assert.False(RecordId.IsRecordLink("table : id"));
    }

    [Fact]
    public void Equality_SameTableAndId_AreEqual()
    {
        var id1 = new RecordId("t", "i");
        var id2 = new RecordId("t", "i");
        Assert.Equal(id1, id2);
    }
}
