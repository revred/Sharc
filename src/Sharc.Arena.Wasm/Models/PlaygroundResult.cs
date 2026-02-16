// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// Result of an interactive SQL Playground query comparing Sharc vs SQLite execution.
/// </summary>
public sealed class PlaygroundResult
{
    public required string Sql { get; init; }

    // Sharc side
    public double SharcTimeUs { get; init; }
    public string SharcAlloc { get; init; } = "";
    public long SharcRowCount { get; init; }
    public string? SharcError { get; init; }

    // SQLite side
    public double SqliteTimeUs { get; init; }
    public string SqliteAlloc { get; init; } = "";
    public long SqliteRowCount { get; init; }
    public string? SqliteError { get; init; }

    // Result grid (from Sharc execution — or SQLite if Sharc fails)
    public List<string> ColumnNames { get; init; } = [];
    public List<List<object?>> Rows { get; init; } = [];

    // Computed
    public string Winner => SharcError is not null ? "sqlite"
                          : SqliteError is not null ? "sharc"
                          : SharcTimeUs <= SqliteTimeUs ? "sharc" : "sqlite";

    public double Speedup
    {
        get
        {
            if (SharcError is not null || SqliteError is not null) return 0;
            return Winner == "sharc"
                ? SqliteTimeUs / Math.Max(0.1, SharcTimeUs)
                : SharcTimeUs / Math.Max(0.1, SqliteTimeUs);
        }
    }
}
