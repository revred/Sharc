// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Reads context entries from workspace.arc.
/// </summary>
public static class GetCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        bool all = false;
        string? key = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--all": all = true; break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal) && key == null)
                        key = args[i];
                    break;
            }
        }

        if (key == null && !all)
        {
            Console.Error.WriteLine("Usage: sharc get <key> | sharc get --all");
            return 1;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new WorkspaceReader(db);

        if (all)
        {
            var entries = reader.ReadContext();
            foreach (var entry in entries)
                Console.WriteLine($"{entry.Key} = {entry.Value}");
            return 0;
        }

        var results = reader.ReadContext(key: key);
        if (results.Count == 0)
        {
            Console.Error.WriteLine($"Key not found: {key}");
            return 1;
        }

        Console.WriteLine(results[0].Value);
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc get <key> | sharc get --all");
    }
}
