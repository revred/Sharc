/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Sharc.Comparisons;

/// <summary>
/// Graph + core comparison benchmark suite entry point.
/// Run with: dotnet run -c Release --project bench/Sharc.Comparisons
///
/// Tier examples (fastest to slowest):
///   -- --tier micro                    ~4 benchmarks,  ~1 min   Engine load + scan
///   -- --tier mini                     ~10 benchmarks, ~3 min   Core pairs + graph seek
///   -- --tier standard                 ~26 benchmarks, ~8 min   Core + graph scan/seek
///   -- --tier mega                     All benchmarks, ~12 min  Everything incl. traversal
///   -- --tier full                     (same as mega)
///
/// Filter examples:
///   -- --filter *Graph*                All graph benchmarks
///   -- --filter *GraphScan*            Node/edge scan benchmarks
///   -- --filter *CoreBenchmarks*       Core comparison benchmarks
///   -- --list flat                     List all benchmarks without running
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        args = ResolveTier(args);
        var artifactsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "benchmarks", "comparisons"));
        var config = DefaultConfig.Instance.WithArtifactsPath(artifactsPath);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }

    private static string[] ResolveTier(string[] args)
    {
        int idx = Array.IndexOf(args, "--tier");
        if (idx < 0 || idx + 1 >= args.Length)
            return args;

        string tierName = args[idx + 1];

        if (!BenchmarkTiers.IsKnown(tierName))
        {
            Console.WriteLine($"Unknown tier '{tierName}'. Available: micro, mini, standard, mega, full");
            return args;
        }

        string[]? filters = BenchmarkTiers.GetFilters(tierName);
        if (filters is null)
        {
            return args.Where((_, i) => i != idx && i != idx + 1).ToArray();
        }

        var result = new List<string> { "--filter" };
        result.AddRange(filters);
        result.AddRange(args.Where((_, i) => i != idx && i != idx + 1));
        return result.ToArray();
    }
}
