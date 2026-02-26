// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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

public class GenericSchemaAdapterTests
{
    [Fact]
    public void Constructor_CanSetProperties()
    {
        var adapter = new GenericSchemaAdapter
        {
            NodeTableName = "n_table",
            EdgeTableName = "e_table",
            NodeIdColumn = "pk",
            NodeKeyColumn = "k",
            NodeTypeColumn = "t",
            NodeDataColumn = "d",
            EdgeOriginColumn = "orig",
            EdgeTargetColumn = "dest",
            EdgeKindColumn = "rel",
            EdgeDataColumn = "props"
        };
        
        Assert.Equal("n_table", adapter.NodeTableName);
        Assert.Equal("e_table", adapter.EdgeTableName);
        Assert.Equal("pk", adapter.NodeIdColumn);
        Assert.Equal("k", adapter.NodeKeyColumn);
        Assert.Equal("orig", adapter.EdgeOriginColumn);
    }
}