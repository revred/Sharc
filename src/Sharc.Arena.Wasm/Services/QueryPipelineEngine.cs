// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Net.Http.Json;
using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Loads query pipeline benchmark data from JSON reference.
/// In a future version, this can run live Sharc.Query() vs SqliteCommand comparisons.
/// </summary>
public sealed class QueryPipelineEngine
{
    private readonly HttpClient _http;
    private IReadOnlyList<QueryResult>? _results;

    public QueryPipelineEngine(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<QueryResult>> LoadResultsAsync()
    {
        _results ??= await _http.GetFromJsonAsync("data/query-benchmarks.json", ArenaJsonContext.Default.ListQueryResult) ?? [];
        return _results;
    }
}
