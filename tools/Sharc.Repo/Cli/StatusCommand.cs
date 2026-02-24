// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Displays workspace status: .sharc/ location, table counts, config summary.
/// </summary>
public static class StatusCommand
{
    public static int Run(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--help") { PrintHelp(); return 0; }
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        Console.WriteLine($"Workspace: {sharcDir}");
        Console.WriteLine();

        // Table counts
        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        if (File.Exists(wsPath))
        {
            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);

            Console.WriteLine("| Table | Rows |");
            Console.WriteLine("|-------|------|");

            var tables = new[] { "commits", "file_changes", "notes", "file_annotations", "decisions", "context", "conversations" };
            foreach (var table in tables)
            {
                try
                {
                    int count = reader.CountRows(table);
                    Console.WriteLine($"| {table} | {count} |");
                }
                catch
                {
                    Console.WriteLine($"| {table} | - |");
                }
            }
            Console.WriteLine();

            // Last update
            var lastSha = reader.GetMeta("last_indexed_sha");
            Console.WriteLine(lastSha != null
                ? $"Last indexed commit: {lastSha}"
                : "Git history not yet indexed. Run 'sharc update'.");
        }

        // Config summary
        Console.WriteLine();
        var configPath = Path.Combine(sharcDir, RepoLocator.ConfigFileName);
        if (File.Exists(configPath))
        {
            using var configDb = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = false });
            using var cw = new ConfigWriter(configDb);
            var entries = cw.GetAll();

            Console.WriteLine("Config:");
            foreach (var (key, value) in entries)
                Console.WriteLine($"  {key} = {value}");
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc status");
        Console.WriteLine();
        Console.WriteLine("Show workspace status: location, table counts, config summary.");
    }
}
