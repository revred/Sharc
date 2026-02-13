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

using BenchmarkDotNet.Running;

namespace Sharc.Benchmarks;

/// <summary>
/// Benchmark suite entry point.
/// Run with: dotnet run -c Release --project bench/Sharc.Benchmarks
///
/// Tier examples (fastest to slowest):
///   -- --tier micro                   6 benchmarks,   ~1.5 min  Quick sanity check
///   -- --tier mini                    14 benchmarks,  ~4 min    PR validation
///   -- --tier standard                76 benchmarks,  ~20 min   All Sharc vs SQLite
///   -- --tier mega                    205 benchmarks, ~50 min   Full suite
///   -- --tier full                    (same as mega)
///
/// Filter examples:
///   -- --filter *Micro*               All micro-benchmarks
///   -- --filter *Comparative*         All comparative benchmarks (Sharc vs SQLite)
///   -- --filter *VarintBenchmarks*    Specific class
///   -- --list flat                    List all benchmarks without running
///
/// Combine tier with other flags:
///   -- --tier mini --exporters json
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        args = ResolveTier(args);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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
            // "full" — remove --tier arg, pass everything else through
            return args.Where((_, i) => i != idx && i != idx + 1).ToArray();
        }

        // Replace --tier with --filter patterns
        var result = new List<string> { "--filter" };
        result.AddRange(filters);
        result.AddRange(args.Where((_, i) => i != idx && i != idx + 1));
        return result.ToArray();
    }
}
