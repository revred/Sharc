using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace Sharc.McpServer.Tools;

[McpServerToolType]
public static class BenchmarkTool
{
    private static readonly string SolutionRoot = FindSolutionRoot();
    private static readonly string ArtifactsPath =
        Path.Combine(SolutionRoot, "BenchmarkDotNet.Artifacts", "results");

    [McpServerTool, Description(
        "Run BenchmarkDotNet comparative benchmarks with optional filter. " +
        "Streams output incrementally so you can see results as each benchmark completes. " +
        "Default filter runs all comparative benchmarks (~15-20 min).")]
    public static async Task<string> RunBenchmarks(
        [Description("BenchmarkDotNet filter pattern (e.g., '*TableScan*', '*TypeDecode*', '*Realistic*'). Default: '*Comparative*'")]
        string filter = "*Comparative*",
        [Description("Job type: 'short' (3 iterations, fast), 'medium' (default BDN), 'dry' (no actual run, validates setup)")]
        string job = "short")
    {
        var args = new StringBuilder("run -c Release --project bench/Sharc.Benchmarks --");
        args.Append($" --filter {filter}");

        if (job.Equals("short", StringComparison.OrdinalIgnoreCase))
            args.Append(" --job short");
        else if (job.Equals("dry", StringComparison.OrdinalIgnoreCase))
            args.Append(" --job dry");

        args.Append(" --memory");

        return await RunDotnetCommandAsync(args.ToString());
    }

    [McpServerTool, Description(
        "Read the latest benchmark results from BenchmarkDotNet artifacts. " +
        "Returns the markdown report for the specified benchmark class.")]
    public static string ReadBenchmarkResults(
        [Description("Benchmark class name (e.g., 'TableScanBenchmarks', 'TypeDecodeBenchmarks', 'RealisticWorkloadBenchmarks', 'GcPressureBenchmarks', 'SchemaMetadataBenchmarks', 'PrimitiveBenchmarks')")]
        string className)
    {
        var pattern = $"*{className}*-report-github.md";
        if (!Directory.Exists(ArtifactsPath))
            return $"No benchmark artifacts found at {ArtifactsPath}. Run benchmarks first.";

        var files = Directory.GetFiles(ArtifactsPath, pattern);
        if (files.Length == 0)
            return $"No results matching '{pattern}' in {ArtifactsPath}. Available: {string.Join(", ", ListAvailableResults())}";

        var sb = new StringBuilder();
        foreach (var file in files.OrderBy(f => f))
        {
            sb.AppendLine($"## {Path.GetFileNameWithoutExtension(file)}");
            sb.AppendLine();
            sb.AppendLine(File.ReadAllText(file));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "List all available benchmark result files from the latest run.")]
    public static string ListBenchmarkResults()
    {
        var results = ListAvailableResults();
        if (results.Length == 0)
            return "No benchmark results found. Run benchmarks first.";

        var sb = new StringBuilder("Available benchmark results:\n\n");
        foreach (var file in results)
        {
            var info = new FileInfo(file);
            sb.AppendLine($"- {Path.GetFileNameWithoutExtension(file)} ({info.Length / 1024.0:F1} KB, {info.LastWriteTime:yyyy-MM-dd HH:mm})");
        }
        return sb.ToString();
    }

    private static string[] ListAvailableResults()
    {
        if (!Directory.Exists(ArtifactsPath))
            return [];
        return Directory.GetFiles(ArtifactsPath, "*-report-github.md");
    }

    private static async Task<string> RunDotnetCommandAsync(string arguments)
    {
        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = SolutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = ReadStreamAsync(process.StandardOutput, sb);
        var stderrTask = ReadStreamAsync(process.StandardError, sb);

        // Benchmarks can take a long time
        var timeout = TimeSpan.FromMinutes(30);
        var completed = process.WaitForExit(timeout);

        await Task.WhenAll(stdoutTask, stderrTask);
        sw.Stop();

        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            sb.AppendLine($"\n[TIMEOUT] Process killed after {timeout.TotalMinutes} minutes.");
        }

        sb.AppendLine($"\n[Duration: {sw.Elapsed.TotalSeconds:F1}s | Exit code: {process.ExitCode}]");
        return sb.ToString();
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder sb)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            sb.AppendLine(line);
        }
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Sharc.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Sharc.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
