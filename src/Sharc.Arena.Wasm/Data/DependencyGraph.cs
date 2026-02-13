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

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Arena.Wasm.Data;

/// <summary>
/// Defines the prerequisite chain between slides.
/// A slide can only run after its prerequisites are complete.
/// </summary>
public static class DependencyGraph
{
    /// <summary>
    /// Returns prerequisites for a given slide ID.
    /// Slides with no prerequisites return an empty array.
    /// </summary>
    public static IReadOnlyList<string> GetPrerequisites(string slideId) => slideId switch
    {
        "schema-read"     => ["engine-load"],
        "sequential-scan" => ["engine-load"],
        "point-lookup"    => ["engine-load"],
        "batch-lookup"    => ["point-lookup"],
        "type-decode"     => ["sequential-scan"],
        "null-scan"       => ["sequential-scan"],
        "where-filter"    => ["sequential-scan"],
        "graph-node-scan" => ["engine-load"],
        "graph-edge-scan" => ["graph-node-scan"],
        "graph-seek"      => ["graph-node-scan"],
        "graph-traverse"  => ["graph-seek", "graph-edge-scan"],
        "gc-pressure"     => ["sequential-scan"],
        "encryption"      => ["engine-load"],
        _                 => [],
    };
}
