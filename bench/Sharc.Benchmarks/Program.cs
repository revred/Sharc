/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
