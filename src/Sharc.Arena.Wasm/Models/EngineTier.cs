// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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