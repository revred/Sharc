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
/// Defines a single benchmark slide with its base results and density configuration.
/// </summary>
public sealed class SlideDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Icon { get; init; }
    public required string Unit { get; init; }
    public required string CategoryId { get; init; }
    public required string Methodology { get; init; }
    public required IReadOnlyList<DensityTier> DensityTiers { get; init; }
    public required string DefaultDensity { get; init; }
    public required string ScaleMode { get; init; }

    /// <summary>Base results keyed by engine ID (at scale=1).</summary>
    public required IReadOnlyDictionary<string, EngineBaseResult> BaseResults { get; init; }
}
