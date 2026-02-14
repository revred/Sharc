// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Interface for a benchmark engine that can run a slide and return scaled results.
/// Phase 1 uses <see cref="ReferenceEngine"/> (static base data).
/// Phase 2+ replaces with live engines (Sharc, SQLite, SurrealDB, IndexedDB).
/// </summary>
public interface IBenchmarkEngine
{
    /// <summary>
    /// Run a benchmark slide at the given scale and return results keyed by engine ID.
    /// </summary>
    Task<IReadOnlyDictionary<string, EngineBaseResult>> RunSlideAsync(
        SlideDefinition slide, double scale, CancellationToken cancellationToken = default);
}