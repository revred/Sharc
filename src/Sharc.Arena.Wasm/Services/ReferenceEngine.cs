/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Reference engine that returns static base data with sublinear scaling and jitter.
/// Used in Phase 1 before live engines are connected.
/// </summary>
public sealed class ReferenceEngine : IBenchmarkEngine
{
    private readonly Random _rng = new(42);

    public Task<IReadOnlyDictionary<string, EngineBaseResult>> RunSlideAsync(
        SlideDefinition slide, double scale, CancellationToken cancellationToken = default)
    {
        var results = ScaleResults(slide.BaseResults, scale, slide.ScaleMode);
        return Task.FromResult(results);
    }

    private IReadOnlyDictionary<string, EngineBaseResult> ScaleResults(
        IReadOnlyDictionary<string, EngineBaseResult> baseResults,
        double scale,
        string scaleMode)
    {
        if (scaleMode == "fixed" || Math.Abs(scale - 1.0) < 0.001)
            return baseResults;

        var output = new Dictionary<string, EngineBaseResult>(baseResults.Count);
        foreach (var (engineId, result) in baseResults)
        {
            if (result.NotSupported || result.Value is null)
            {
                output[engineId] = result;
                continue;
            }

            // Sublinear scaling for more realistic behavior (matches JSX: Math.pow(scale, 0.85))
            var factor = Math.Pow(scale, 0.85);
            var jitter = 0.95 + _rng.NextDouble() * 0.1;
            var scaledValue = result.Value.Value * factor * jitter;

            // Round to 4 significant figures
            scaledValue = RoundToSignificantDigits(scaledValue, 4);

            output[engineId] = new EngineBaseResult
            {
                Value = scaledValue,
                Allocation = result.Allocation,
                Note = result.Note,
                NotSupported = false,
            };
        }

        return output;
    }

    private static double RoundToSignificantDigits(double value, int digits)
    {
        if (value == 0) return 0;
        var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) + 1);
        return scale * Math.Round(value / scale, digits);
    }
}
