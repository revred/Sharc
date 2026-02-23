// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace Sharc.Context.Tools;

/// <summary>
/// MCP tool for running the Sharc test suite and streaming incremental results.
/// </summary>
[McpServerToolType]
public static class TestRunnerTool
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    [McpServerTool, Description(
        "Run all Sharc tests (unit + integration) and stream results incrementally. " +
        "Returns pass/fail counts as they complete. Use filter to run specific test classes.")]
    public static async Task<string> RunTests(
        [Description("Optional test filter (e.g., 'VarintDecoderTests', 'BTree'). Empty runs all.")]
        string filter = "",
        [Description("Which tests to run: 'all', 'unit', 'integration'")]
        string scope = "all")
    {
        var projectArg = scope.ToLowerInvariant() switch
        {
            "unit" => "tests/Sharc.Tests",
            "integration" => "tests/Sharc.IntegrationTests",
            _ => ""
        };

        var args = new StringBuilder("test");
        if (!string.IsNullOrWhiteSpace(projectArg))
            args.Append($" {projectArg}");
        args.Append(" --verbosity normal --no-restore");
        if (!string.IsNullOrWhiteSpace(filter))
            args.Append($" --filter \"FullyQualifiedName~{filter}\"");

        return await RunDotnetCommandAsync(args.ToString());
    }

    [McpServerTool, Description(
        "Run a quick build check (dotnet build) and return warnings/errors. " +
        "Fast feedback on whether the code compiles.")]
    public static async Task<string> BuildCheck()
    {
        return await RunDotnetCommandAsync("build --verbosity minimal --no-restore");
    }

    [McpServerTool, Description(
        "Get current test status by running tests in minimal verbosity. " +
        "Returns summary: passed/failed/skipped counts.")]
    public static async Task<string> TestStatus()
    {
        return await RunDotnetCommandAsync("test --verbosity minimal --no-restore");
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

        // Read both stdout and stderr concurrently
        var stdoutTask = ReadStreamAsync(process.StandardOutput, sb);
        var stderrTask = ReadStreamAsync(process.StandardError, sb);

        var timeout = TimeSpan.FromMinutes(10);
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
        // Fallback: walk up from current directory
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
