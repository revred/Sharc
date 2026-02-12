using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace Sharc.McpServer.Tools;

[McpServerToolType]
public static class ProjectStatusTool
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    [McpServerTool, Description(
        "Get a comprehensive project health snapshot: build status, test counts, " +
        "file counts by project, and git status.")]
    public static async Task<string> ProjectHealth()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Sharc Project Health");
        sb.AppendLine();

        // Git status
        var gitStatus = await RunCommandAsync("git", "status --porcelain");
        var gitBranch = await RunCommandAsync("git", "branch --show-current");
        var gitLog = await RunCommandAsync("git", "log --oneline -5");
        sb.AppendLine($"## Git");
        sb.AppendLine($"- Branch: `{gitBranch.Trim()}`");
        sb.AppendLine($"- Working tree: {(string.IsNullOrWhiteSpace(gitStatus) ? "CLEAN" : $"{gitStatus.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} modified files")}");
        sb.AppendLine($"- Recent commits:");
        foreach (var line in gitLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(5))
            sb.AppendLine($"  - {line.Trim()}");
        sb.AppendLine();

        // File counts
        sb.AppendLine("## Source Files");
        var srcProjects = new[] { "src/Sharc", "src/Sharc.Core", "src/Sharc.Crypto" };
        var testProjects = new[] { "tests/Sharc.Tests", "tests/Sharc.IntegrationTests" };
        var benchProjects = new[] { "bench/Sharc.Benchmarks" };

        int totalSrc = 0, totalTest = 0;
        foreach (var proj in srcProjects)
        {
            var path = Path.Combine(SolutionRoot, proj.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
            {
                var count = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                    .Count(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
                totalSrc += count;
                sb.AppendLine($"- {proj}: {count} .cs files");
            }
        }
        foreach (var proj in testProjects)
        {
            var path = Path.Combine(SolutionRoot, proj.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
            {
                var count = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                    .Count(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
                totalTest += count;
                sb.AppendLine($"- {proj}: {count} .cs files");
            }
        }
        foreach (var proj in benchProjects)
        {
            var path = Path.Combine(SolutionRoot, proj.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
            {
                var count = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                    .Count(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
                sb.AppendLine($"- {proj}: {count} .cs files");
            }
        }
        sb.AppendLine($"- **Total**: {totalSrc} source + {totalTest} test files");
        sb.AppendLine();

        // Quick build check
        sb.AppendLine("## Build");
        var buildResult = await RunCommandAsync("dotnet", "build --verbosity quiet --no-restore");
        var buildExitCode = buildResult.Contains("error") ? "FAILED" : "OK";
        sb.AppendLine($"- Status: **{buildExitCode}**");
        if (buildResult.Contains("error"))
            sb.AppendLine($"- Errors: {buildResult}");
        sb.AppendLine();

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Read a specific source file from the Sharc project. " +
        "Path is relative to solution root (e.g., 'src/Sharc/SharcDatabase.cs').")]
    public static string ReadFile(
        [Description("Relative path from solution root")]
        string relativePath)
    {
        var fullPath = Path.Combine(SolutionRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return $"File not found: {fullPath}";

        var content = File.ReadAllText(fullPath);
        if (content.Length > 50_000)
            return $"File is large ({content.Length:N0} chars). Showing first 50,000 chars:\n\n{content[..50_000]}\n\n... [truncated]";

        return content;
    }

    [McpServerTool, Description(
        "Search for a pattern in the Sharc source code. " +
        "Returns matching file paths and line numbers.")]
    public static async Task<string> SearchCode(
        [Description("Search pattern (supports regex)")]
        string pattern,
        [Description("File glob filter (e.g., '*.cs', 'src/**/*.cs'). Default: '*.cs'")]
        string glob = "*.cs")
    {
        // Use dotnet's built-in grep equivalent or fall back to findstr/grep
        var result = await RunCommandAsync("git", $"grep -n \"{pattern}\" -- \"{glob}\"");
        if (string.IsNullOrWhiteSpace(result))
            return $"No matches for '{pattern}' in {glob}";

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 100)
            return $"Found {lines.Length} matches. Showing first 100:\n\n{string.Join('\n', lines.Take(100))}\n\n... [{lines.Length - 100} more]";

        return $"Found {lines.Length} matches:\n\n{result}";
    }

    private static async Task<string> RunCommandAsync(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = SolutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
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
