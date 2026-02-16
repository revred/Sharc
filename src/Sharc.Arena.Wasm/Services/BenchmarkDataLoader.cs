// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Source-generated JSON context for AOT-compatible deserialization.
/// </summary>
[JsonSerializable(typeof(List<EngineDefinition>))]
[JsonSerializable(typeof(List<Category>))]
[JsonSerializable(typeof(List<Preset>))]
[JsonSerializable(typeof(List<SlideDefinition>))]
[JsonSerializable(typeof(List<QueryResult>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ArenaJsonContext : JsonSerializerContext;

/// <summary>
/// Loads benchmark data from JSON files in wwwroot/data/.
/// Caches results after first load.
/// </summary>
public sealed class BenchmarkDataLoader
{
    private readonly HttpClient _http;

    private IReadOnlyList<EngineDefinition>? _engines;
    private IReadOnlyList<Category>? _categories;
    private IReadOnlyList<Preset>? _presets;
    private IReadOnlyList<SlideDefinition>? _slides;

    public BenchmarkDataLoader(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<EngineDefinition>> LoadEnginesAsync()
    {
        _engines ??= await _http.GetFromJsonAsync("data/engines.json", ArenaJsonContext.Default.ListEngineDefinition) ?? [];
        return _engines;
    }

    public async Task<IReadOnlyList<Category>> LoadCategoriesAsync()
    {
        _categories ??= await _http.GetFromJsonAsync("data/categories.json", ArenaJsonContext.Default.ListCategory) ?? [];
        return _categories;
    }

    public async Task<IReadOnlyList<Preset>> LoadPresetsAsync()
    {
        _presets ??= await _http.GetFromJsonAsync("data/presets.json", ArenaJsonContext.Default.ListPreset) ?? [];
        return _presets;
    }

    public async Task<IReadOnlyList<SlideDefinition>> LoadAllSlidesAsync()
    {
        if (_slides is not null) return _slides;

        var tasks = new[]
        {
            _http.GetFromJsonAsync("data/core-benchmarks.json", ArenaJsonContext.Default.ListSlideDefinition),
            _http.GetFromJsonAsync("data/scan-benchmarks.json", ArenaJsonContext.Default.ListSlideDefinition),
            _http.GetFromJsonAsync("data/graph-benchmarks.json", ArenaJsonContext.Default.ListSlideDefinition),
            _http.GetFromJsonAsync("data/trust-benchmarks.json", ArenaJsonContext.Default.ListSlideDefinition),
        };

        await Task.WhenAll(tasks);

        var all = new List<SlideDefinition>();
        foreach (var task in tasks)
        {
            var result = await task;
            if (result is not null) all.AddRange(result);
        }

        _slides = all;
        return _slides;
    }
}
