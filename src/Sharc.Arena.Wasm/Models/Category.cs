// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// A benchmark category (Core, Advanced, Graph, Meta) with display color.
/// </summary>
public sealed class Category
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Color { get; init; }
}