// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// Result of a single query comparison between Sharc and SQLite.
/// </summary>
public sealed class QueryResult
{
    public required string Id { get; init; }
    public required string Query { get; init; }
    public required string Description { get; init; }
    public double SharcTimeUs { get; init; }
    public string SharcAlloc { get; init; } = "";
    public double SqliteTimeUs { get; init; }
    public string SqliteAlloc { get; init; } = "";
    public string Winner { get; init; } = "";
    public double Speedup { get; init; }
}
