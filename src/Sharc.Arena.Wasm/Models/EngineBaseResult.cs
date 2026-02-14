// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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