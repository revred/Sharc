using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Sharc.Comparisons;

/// <summary>
/// Stable benchmark profile for join comparisons.
/// Uses more warmup/measurement iterations to reduce run-to-run variance.
/// </summary>
internal sealed class JoinStabilityConfig : ManualConfig
{
    public JoinStabilityConfig()
    {
        AddJob(
            Job.Default
                .WithLaunchCount(1)
                .WithWarmupCount(8)
                .WithIterationCount(24)
                .WithId("Stable"));

        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
