// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Scan;
using Sharc.Repo.Schema;

namespace Sharc.Repo.Cli;

/// <summary>
/// Scans the codebase and populates knowledge graph tables in workspace.arc.
/// Usage: sharc scan [--dry-run] [--full]
/// </summary>
public static class ScanCommand
{
    public static int Run(string[] args)
    {
        bool dryRun = false;
        bool full = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dry-run": dryRun = true; break;
                case "--full": full = true; break;
                case "--help": PrintHelp(); return 0;
            }
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var gitRoot = RepoLocator.FindGitRoot();
        if (gitRoot == null)
        {
            Console.Error.WriteLine("Error: Not inside a git repository.");
            return 1;
        }

        Console.WriteLine("Scanning codebase...");
        var scanner = new CodebaseScanner(gitRoot);
        var scanResult = scanner.Scan();

        Console.WriteLine($"  Features:      {scanResult.Features.Count}");
        Console.WriteLine($"  Feature edges: {scanResult.FeatureEdges.Count}");
        Console.WriteLine($"  File purposes: {scanResult.FilePurposes.Count}");
        Console.WriteLine($"  File deps:     {scanResult.FileDeps.Count}");

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run — no changes written.");
            return 0;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });

        using var writer = new KnowledgeWriter(db);

        if (full)
        {
            Console.WriteLine("Full scan: clearing auto-detected entries...");
            writer.ClearAutoDetected();
        }

        int features = 0, edges = 0, purposes = 0, deps = 0;

        foreach (var f in scanResult.Features)
        {
            if (writer.WriteFeature(f) >= 0) features++;
        }

        foreach (var fp in scanResult.FilePurposes)
        {
            if (writer.WriteFilePurpose(fp) >= 0) purposes++;
        }

        foreach (var e in scanResult.FeatureEdges)
        {
            writer.WriteFeatureEdge(e);
            edges++;
        }

        foreach (var d in scanResult.FileDeps)
        {
            writer.WriteFileDep(d);
            deps++;
        }

        Console.WriteLine();
        Console.WriteLine($"Written: {features} features, {edges} edges, {purposes} file purposes, {deps} deps");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc scan [--dry-run] [--full]");
        Console.WriteLine();
        Console.WriteLine("Scan the codebase and populate knowledge graph tables.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --dry-run  Show scan results without writing to workspace.arc");
        Console.WriteLine("  --full     Clear auto-detected entries before re-scanning");
    }
}
