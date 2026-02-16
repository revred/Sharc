// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Interface for a benchmark engine that can run a slide and return scaled results.
/// Live engines (Sharc, SQLite, IndexedDB) run real benchmarks;
/// <see cref="ReferenceEngine"/> provides static baseline data as fallback.
/// </summary>
public interface IBenchmarkEngine
{
    /// <summary>
    /// Run a benchmark slide at the given scale and return results keyed by engine ID.
    /// </summary>
    Task<IReadOnlyDictionary<string, EngineBaseResult>> RunSlideAsync(
        SlideDefinition slide, double scale, CancellationToken cancellationToken = default);
}