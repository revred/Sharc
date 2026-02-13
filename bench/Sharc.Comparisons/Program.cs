/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Running;

namespace Sharc.Comparisons;

/// <summary>
/// Graph benchmark suite entry point.
/// Run with: dotnet run -c Release --project bench/Sharc.Comparisons
///
/// Filter examples:
///   --filter *Graph*                   All graph benchmarks
///   --filter *GraphScan*              Node/edge scan benchmarks
///   --filter *GraphSeek*              Point lookup benchmarks
///   --list flat                        List all benchmarks without running
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
