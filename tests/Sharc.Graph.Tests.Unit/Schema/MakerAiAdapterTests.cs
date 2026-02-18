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
using Xunit;

namespace Sharc.Graph.Tests.Unit.Schema;

public class MakerAiAdapterTests
{
    private readonly MakerAiAdapter _adapter = new();

    [Fact]
    public void TableNames_AreCorrect()
    {
        Assert.Equal("Entity", _adapter.NodeTableName);
        Assert.Equal("Edge", _adapter.EdgeTableName);
        Assert.Equal("EdgeHistory", _adapter.EdgeHistoryTableName);
        Assert.Null(_adapter.MetaTableName);
    }

    [Fact]
    public void ColumnMapping_Entity_AllColumnsPresent()
    {
        Assert.Equal("GUID", _adapter.NodeIdColumn);
        Assert.Equal("BarID", _adapter.NodeKeyColumn);
        Assert.Equal("TypeID", _adapter.NodeTypeColumn);
        Assert.Equal("Details", _adapter.NodeDataColumn);
        Assert.Equal("CVN", _adapter.NodeCvnColumn);
        Assert.Equal("LVN", _adapter.NodeLvnColumn);
        Assert.Equal("SyncStatus", _adapter.NodeSyncColumn);
        Assert.Equal("LastUpdatedUTC", _adapter.NodeUpdatedColumn);
    }
    
    [Fact]
    public void ColumnMapping_Edge_AllColumnsPresent()
    {
        Assert.Equal("GUID", _adapter.EdgeIdColumn);
        Assert.Equal("OriginID", _adapter.EdgeOriginColumn);
        Assert.Equal("TargetID", _adapter.EdgeTargetColumn);
        Assert.Equal("LinkID", _adapter.EdgeKindColumn);
        Assert.Equal("Details", _adapter.EdgeDataColumn);
        Assert.Equal("CVN", _adapter.EdgeCvnColumn);
        Assert.Equal("LVN", _adapter.EdgeLvnColumn);
        Assert.Equal("SyncStatus", _adapter.EdgeSyncColumn);
    }

    [Fact]
    public void TypeNames_Contains_CommonTypes()
    {
        var types = _adapter.TypeNames;
        
        Assert.Equal("project", types[1]);
        Assert.Equal("shopjob", types[2]);
        Assert.Equal("operation", types[3]);
        Assert.Equal("capability", types[13]);
    }

    [Fact]
    public void RequiredIndexDDL_HasBarIdIndex()
    {
        var ddl = _adapter.RequiredIndexDDL;
        bool hasBarId = ddl.Any(x => x.Contains("Entity") && x.Contains("BarID"));
        Assert.True(hasBarId, "Should index BarID");
    }
}
