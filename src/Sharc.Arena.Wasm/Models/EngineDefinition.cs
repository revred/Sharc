// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// Defines a benchmark engine with its display metadata and tier classification.
/// </summary>
public sealed class EngineDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Subtitle { get; init; }
    public required string Color { get; init; }
    public required string Icon { get; init; }
    public required string Footprint { get; init; }
    public required EngineTier Tier { get; init; }
}