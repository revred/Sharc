// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// A per-tile density control tier defining row count and scale factor.
/// </summary>
public sealed class DensityTier
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required int Rows { get; init; }
    public required double Scale { get; init; }
}