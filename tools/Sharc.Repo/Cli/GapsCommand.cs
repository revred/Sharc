// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Gap analysis: identifies missing tests, docs, and orphan files.
/// Usage: sharc gaps [--tests] [--docs] [--orphans]
/// </summary>
public static class GapsCommand
{
    public static int Run(string[] args)
    {
        bool showTests = false;
        bool showDocs = false;
        bool showOrphans = false;
        bool showAll = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tests": showTests = true; showAll = false; break;
                case "--docs": showDocs = true; showAll = false; break;
                case "--orphans": showOrphans = true; showAll = false; break;
                case "--help": PrintHelp(); return 0;
            }
        }

        if (showAll) { showTests = true; showDocs = true; showOrphans = true; }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);

        bool anyGaps = false;

        if (showDocs)
        {
            var gapped = reader.FindFeaturesWithoutDocs();
            if (gapped.Count > 0)
            {
                anyGaps = true;
                Console.WriteLine($"Features without documentation ({gapped.Count}):");
                foreach (var name in gapped)
                    Console.WriteLine($"  {name}");
                Console.WriteLine();
            }
        }

        if (showTests)
        {
            var untested = reader.FindFilesWithoutTests();
            if (untested.Count > 0)
            {
                anyGaps = true;
                Console.WriteLine($"Source files in untested features ({untested.Count}):");
                foreach (var path in untested)
                    Console.WriteLine($"  {path}");
                Console.WriteLine();
            }
        }

        if (showOrphans)
        {
            var orphans = reader.FindOrphanFiles();
            if (orphans.Count > 0)
            {
                anyGaps = true;
                Console.WriteLine($"Orphan files (no feature mapping) ({orphans.Count}):");
                foreach (var path in orphans)
                    Console.WriteLine($"  {path}");
                Console.WriteLine();
            }
        }

        if (!anyGaps)
            Console.WriteLine("No gaps found.");

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc gaps [--tests] [--docs] [--orphans]");
        Console.WriteLine();
        Console.WriteLine("Analyze the knowledge graph for coverage gaps.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --tests    Show source files in features without test edges");
        Console.WriteLine("  --docs     Show features without documentation edges");
        Console.WriteLine("  --orphans  Show files not mapped to any feature");
        Console.WriteLine();
        Console.WriteLine("With no options, all gap types are shown.");
    }
}
