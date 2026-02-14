// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// A workload preset that defines scale, pause timing, and transition timing.
/// </summary>
public sealed class Preset
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required double Scale { get; init; }
    public required int PauseMs { get; init; }
    public required int TransitionMs { get; init; }
}