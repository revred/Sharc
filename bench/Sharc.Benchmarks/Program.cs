using BenchmarkDotNet.Running;

namespace Sharc.Benchmarks;

/// <summary>
/// Benchmark suite entry point.
/// Run with: dotnet run -c Release --project bench/Sharc.Benchmarks
///
/// Filter examples:
///   --filter *Micro*                  All micro-benchmarks
///   --filter *Comparative*            All comparative benchmarks (Sharc vs SQLite)
///   --filter *VarintBenchmarks*       Specific class
///   --filter *VarintBenchmarks.Read*  Specific method pattern
///   --list flat                       List all benchmarks without running
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
