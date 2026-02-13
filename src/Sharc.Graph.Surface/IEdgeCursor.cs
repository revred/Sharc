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

namespace Sharc.Graph;

/// <summary>
/// A forward-only, zero-allocation cursor over graph edges originating from a single node.
/// Avoids <see cref="GraphEdge"/> allocation per row — callers read typed properties directly.
/// The cursor is valid until disposed. Property values are valid until the next <see cref="MoveNext"/> call.
/// </summary>
public interface IEdgeCursor : IDisposable
{
    /// <summary>Advances to the next matching edge. Returns false when exhausted.</summary>
    bool MoveNext();

    /// <summary>The integer key of the origin node.</summary>
    long OriginKey { get; }

    /// <summary>The integer key of the target node.</summary>
    long TargetKey { get; }

    /// <summary>The edge kind/link ID.</summary>
    int Kind { get; }

    /// <summary>Edge relevance weight (0.0 to 1.0).</summary>
    float Weight { get; }

    /// <summary>
    /// The raw UTF-8 bytes of the edge JSON data. Zero-allocation — avoids string materialization.
    /// Returns empty if the data column is not present or is NULL.
    /// </summary>
    ReadOnlyMemory<byte> JsonDataUtf8 { get; }
}
