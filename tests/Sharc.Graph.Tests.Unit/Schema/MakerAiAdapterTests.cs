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
using Sharc.Graph.Schema;

namespace Sharc.Graph.Tests.Unit.Schema;

[TestClass]
public class MakerAiAdapterTests
{
    private readonly MakerAiAdapter _adapter = new();

    [TestMethod]
    public void TableNames_AreCorrect()
    {
        Assert.AreEqual("Entity", _adapter.NodeTableName);
        Assert.AreEqual("Edge", _adapter.EdgeTableName);
        Assert.AreEqual("EdgeHistory", _adapter.EdgeHistoryTableName);
        Assert.IsNull(_adapter.MetaTableName);
    }

    [TestMethod]
    public void ColumnMapping_Entity_AllColumnsPresent()
    {
        Assert.AreEqual("GUID", _adapter.NodeIdColumn);
        Assert.AreEqual("BarID", _adapter.NodeKeyColumn);
        Assert.AreEqual("TypeID", _adapter.NodeTypeColumn);
        Assert.AreEqual("Details", _adapter.NodeDataColumn);
        Assert.AreEqual("CVN", _adapter.NodeCvnColumn);
        Assert.AreEqual("LVN", _adapter.NodeLvnColumn);
        Assert.AreEqual("SyncStatus", _adapter.NodeSyncColumn);
        Assert.AreEqual("LastUpdatedUTC", _adapter.NodeUpdatedColumn);
    }
    
    [TestMethod]
    public void ColumnMapping_Edge_AllColumnsPresent()
    {
        Assert.AreEqual("GUID", _adapter.EdgeIdColumn);
        Assert.AreEqual("OriginID", _adapter.EdgeOriginColumn);
        Assert.AreEqual("TargetID", _adapter.EdgeTargetColumn);
        Assert.AreEqual("LinkID", _adapter.EdgeKindColumn);
        Assert.AreEqual("Details", _adapter.EdgeDataColumn);
        Assert.AreEqual("CVN", _adapter.EdgeCvnColumn);
        Assert.AreEqual("LVN", _adapter.EdgeLvnColumn);
        Assert.AreEqual("SyncStatus", _adapter.EdgeSyncColumn);
    }

    [TestMethod]
    public void TypeNames_Contains_CommonTypes()
    {
        var types = _adapter.TypeNames;
        
        Assert.AreEqual("project", types[1]);
        Assert.AreEqual("shopjob", types[2]);
        Assert.AreEqual("operation", types[3]);
        Assert.AreEqual("capability", types[13]);
    }

    [TestMethod]
    public void RequiredIndexDDL_HasBarIdIndex()
    {
        var ddl = _adapter.RequiredIndexDDL;
        bool hasBarId = ddl.Any(x => x.Contains("Entity") && x.Contains("BarID"));
        Assert.IsTrue(hasBarId, "Should index BarID");
    }
}
