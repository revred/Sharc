// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Feature management: list, show, add, link.
/// Usage: sharc feature &lt;subcommand&gt; [options]
/// </summary>
public static class FeatureCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            PrintHelp();
            return 0;
        }

        return args[0] switch
        {
            "list" => RunList(args[1..]),
            "show" => RunShow(args[1..]),
            "add" => RunAdd(args[1..]),
            "link" => RunLink(args[1..]),
            _ => Error($"Unknown subcommand: {args[0]}")
        };
    }

    private static int RunList(string[] args)
    {
        string? layer = null;
        string? status = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--layer" when i + 1 < args.Length: layer = args[++i]; break;
                case "--status" when i + 1 < args.Length: status = args[++i]; break;
            }
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);

        var features = reader.ReadFeatures(layer, status);
        if (features.Count == 0)
        {
            Console.WriteLine("No features found. Run 'sharc scan' first.");
            return 0;
        }

        Console.WriteLine($"{"Name",-25} {"Layer",-10} {"Status",-12} Description");
        Console.WriteLine(new string('-', 80));

        foreach (var f in features)
        {
            var desc = f.Description ?? "";
            if (desc.Length > 35) desc = desc[..32] + "...";
            Console.WriteLine($"{f.Name,-25} {f.Layer,-10} {f.Status,-12} {desc}");
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {features.Count} features");
        return 0;
    }

    private static int RunShow(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: sharc feature show <name>");
            return 1;
        }

        string name = args[0];

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);

        var feature = reader.GetFeature(name);
        if (feature == null)
        {
            Console.Error.WriteLine($"Feature not found: {name}");
            return 1;
        }

        Console.WriteLine($"Feature: {feature.Name}");
        Console.WriteLine($"  Description: {feature.Description ?? "(none)"}");
        Console.WriteLine($"  Layer:       {feature.Layer}");
        Console.WriteLine($"  Status:      {feature.Status}");
        Console.WriteLine();

        var edges = reader.ReadFeatureEdges(featureName: name);
        var sourceEdges = edges.Where(e => e.TargetKind == "source").ToList();
        var testEdges = edges.Where(e => e.TargetKind == "test").ToList();
        var docEdges = edges.Where(e => e.TargetKind == "doc").ToList();
        var benchEdges = edges.Where(e => e.TargetKind == "bench").ToList();

        if (sourceEdges.Count > 0)
        {
            Console.WriteLine($"Source files ({sourceEdges.Count}):");
            foreach (var e in sourceEdges)
                Console.WriteLine($"  {e.TargetPath}");
            Console.WriteLine();
        }

        if (testEdges.Count > 0)
        {
            Console.WriteLine($"Test files ({testEdges.Count}):");
            foreach (var e in testEdges)
                Console.WriteLine($"  {e.TargetPath}");
            Console.WriteLine();
        }

        if (docEdges.Count > 0)
        {
            Console.WriteLine($"Documentation ({docEdges.Count}):");
            foreach (var e in docEdges)
                Console.WriteLine($"  {e.TargetPath}");
            Console.WriteLine();
        }

        if (benchEdges.Count > 0)
        {
            Console.WriteLine($"Benchmarks ({benchEdges.Count}):");
            foreach (var e in benchEdges)
                Console.WriteLine($"  {e.TargetPath}");
            Console.WriteLine();
        }

        if (edges.Count == 0)
            Console.WriteLine("(no edges — run 'sharc scan' to populate)");

        return 0;
    }

    private static int RunAdd(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: sharc feature add <name> \"description\" --layer <L> --status <S>");
            return 1;
        }

        string name = args[0];
        string description = args[1];
        string layer = "api";
        string status = "active";

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--layer" when i + 1 < args.Length: layer = args[++i]; break;
                case "--status" when i + 1 < args.Length: status = args[++i]; break;
            }
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
        using var writer = new KnowledgeWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var record = new FeatureRecord(name, description, layer, status, now, null);
        long id = writer.WriteFeature(record);

        if (id < 0)
        {
            Console.Error.WriteLine($"Feature '{name}' already exists.");
            return 1;
        }

        Console.WriteLine($"Added feature: {name} (layer={layer}, status={status})");
        return 0;
    }

    private static int RunLink(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: sharc feature link <name> <path> [--kind source|test|doc]");
            return 1;
        }

        string name = args[0];
        string path = args[1].Replace('\\', '/');
        string kind = "source";

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--kind" && i + 1 < args.Length) kind = args[++i];
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
        using var writer = new KnowledgeWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var record = new FeatureEdgeRecord(name, path, kind, null, false, now, null);
        writer.WriteFeatureEdge(record);

        Console.WriteLine($"Linked: {name} -> {path} (kind={kind})");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine(message);
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc feature <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  list [--layer L] [--status S]  List features");
        Console.WriteLine("  show <name>                    Show feature cross-references");
        Console.WriteLine("  add <name> \"desc\" [opts]       Add a manual feature entry");
        Console.WriteLine("  link <name> <path> [--kind K]  Link a file to a feature");
    }
}
