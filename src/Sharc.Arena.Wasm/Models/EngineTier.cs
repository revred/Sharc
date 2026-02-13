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
/// Classifies how an engine runs inside the Blazor WASM runtime.
/// Tier 1 = native .NET WASM (same GC, same JIT/AOT).
/// Tier 2 = JS interop (browser-native API).
/// Deferred = planned but not yet integrated (reference data only).
/// </summary>
public enum EngineTier
{
    /// <summary>Runs natively inside the .NET WASM runtime (C#, C via Emscripten, or Rust via Emscripten).</summary>
    NativeDotNet = 1,

    /// <summary>Requires JavaScript interop (browser-native API like IndexedDB).</summary>
    JsInterop = 2,

    /// <summary>Engine is planned but not yet integrated (reference data only).</summary>
    Deferred = 3
}
