#!/usr/bin/env dotnet-script
using System.Diagnostics;

var options = ParseArgs(Args ?? Array.Empty<string>());
var scriptDirectory = Path.GetDirectoryName(GetScriptPath()) ?? Directory.GetCurrentDirectory();

var sampleProjects = DiscoverSampleProjects(scriptDirectory);
if (sampleProjects.Count == 0)
{
    Console.Error.WriteLine($"No sample projects were found under '{scriptDirectory}'.");
    Environment.Exit(1);
}

var buildFailures = new List<string>();
var runFailures = new List<string>();

foreach (var sample in sampleProjects)
{
    Console.WriteLine();
    Console.WriteLine($"==> Build [{sample.Name}]");

    var buildArgs = new List<string> { "build", sample.ProjectPath, "-c", options.Configuration };
    if (options.NoRestore)
        buildArgs.Add("--no-restore");

    if (RunProcess("dotnet", buildArgs, scriptDirectory) != 0)
        buildFailures.Add(sample.Name);
}

if (buildFailures.Count > 0)
{
    Console.WriteLine();
    Console.Error.WriteLine($"Build failures: {string.Join(", ", buildFailures)}");
    Environment.Exit(1);
}

if (options.BuildOnly)
{
    Console.WriteLine();
    Console.WriteLine("All sample projects built successfully.");
    Environment.Exit(0);
}

foreach (var sample in sampleProjects)
{
    Console.WriteLine();
    Console.WriteLine($"==> Run [{sample.Name}]");

    var runArgs = new[]
    {
        "run", "--project", sample.ProjectPath, "-c", options.Configuration, "--no-build"
    };

    if (RunProcess("dotnet", runArgs, scriptDirectory) != 0)
        runFailures.Add(sample.Name);
}

Console.WriteLine();
if (runFailures.Count > 0)
{
    Console.Error.WriteLine($"Run failures: {string.Join(", ", runFailures)}");
    Environment.Exit(1);
}

Console.WriteLine("All sample projects built and ran successfully.");
Environment.Exit(0);

static ScriptOptions ParseArgs(string[] args)
{
    var configuration = "Release";
    var buildOnly = false;
    var noRestore = false;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "-c":
            case "--configuration":
                if (i + 1 >= args.Length)
                    ExitWithUsage("Missing value for configuration.");
                configuration = args[++i];
                break;
            case "--build-only":
                buildOnly = true;
                break;
            case "--no-restore":
                noRestore = true;
                break;
            case "-h":
            case "--help":
                ExitWithUsage();
                break;
            default:
                ExitWithUsage($"Unknown option: {arg}");
                break;
        }
    }

    if (!string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
    {
        ExitWithUsage($"Unsupported configuration '{configuration}'. Use Debug or Release.");
    }

    return new ScriptOptions(
        char.ToUpperInvariant(configuration[0]) + configuration[1..].ToLowerInvariant(),
        buildOnly,
        noRestore);
}

static void ExitWithUsage(string? error = null)
{
    if (!string.IsNullOrWhiteSpace(error))
        Console.Error.WriteLine(error);

    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet script ./samples/run-all.csx -- [--build-only] [--no-restore] [-c|--configuration Debug|Release]");
    Environment.Exit(string.IsNullOrWhiteSpace(error) ? 0 : 1);
}

static List<SampleProject> DiscoverSampleProjects(string scriptDirectory)
{
    var sampleProjects = new List<SampleProject>();
    foreach (var directory in Directory.EnumerateDirectories(scriptDirectory))
    {
        var projectPath = Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault();
        if (projectPath is null)
            continue;

        sampleProjects.Add(new SampleProject(Path.GetFileName(directory), projectPath));
    }

    sampleProjects.Sort(static (x, y) => string.CompareOrdinal(x.Name, y.Name));
    return sampleProjects;
}

static int RunProcess(string fileName, IEnumerable<string> args, string workingDirectory)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };

    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi);
    if (process is null)
        return 1;

    process.WaitForExit();
    return process.ExitCode;
}

static string GetScriptPath()
{
    // In dotnet-script, #load/file location is available via CallerFilePath in a helper method.
    return GetScriptPathCore();
}

static string GetScriptPathCore([System.Runtime.CompilerServices.CallerFilePath] string path = "")
{
    return path;
}

internal sealed record ScriptOptions(string Configuration, bool BuildOnly, bool NoRestore);
internal sealed record SampleProject(string Name, string ProjectPath);
