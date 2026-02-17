// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Sharc.CacheBenchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        var artifactsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "benchmarks", "cache"));
        var config = DefaultConfig.Instance.WithArtifactsPath(artifactsPath);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
