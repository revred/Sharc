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

public class GraphRecordTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var id = RecordId.Parse("t:i");
        var key = new NodeKey(123);
        var now = DateTimeOffset.UtcNow;
        
        var record = new GraphRecord(id, key, 10, "{}")
        {
            CreatedAt = now,
            CVN = 1,
            LVN = 2,
            SyncStatus = 1
        };

        Assert.Equal(id, record.Id);
        Assert.Equal(key, record.Key);
        Assert.Equal(10, record.TypeId);
        Assert.Equal("{}", record.JsonData);
        Assert.Equal(now, record.CreatedAt);
        Assert.Equal(1, record.CVN);
        Assert.Equal(2, record.LVN);
        Assert.Equal(1, record.SyncStatus);
    }

    [Fact]
    public void CVN_LVN_DefaultToZero()
    {
        var record = new GraphRecord(RecordId.Parse("t:i"), new NodeKey(1), 0, "{}");
        Assert.Equal(0, record.CVN);
        Assert.Equal(0, record.LVN);
    }
}
