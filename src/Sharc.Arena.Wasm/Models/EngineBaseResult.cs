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

namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// A single engine's result for one benchmark slide.
/// </summary>
public sealed class EngineBaseResult
{
    /// <summary>The measured value (null if not applicable).</summary>
    public double? Value { get; init; }

    /// <summary>Allocation string (e.g. "1.5 MB", "0 B").</summary>
    public string? Allocation { get; init; }

    /// <summary>Optional explanatory note.</summary>
    public string? Note { get; init; }

    /// <summary>If true, this engine does not support this benchmark.</summary>
    public bool NotSupported { get; init; }
}
