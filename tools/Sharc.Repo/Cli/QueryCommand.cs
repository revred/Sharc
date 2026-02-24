// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo.Cli;

/// <summary>
/// Generic table query for workspace.arc.
/// </summary>
public static class QueryCommand
{
    private static readonly HashSet<string> ValidTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "commits", "file_changes", "notes", "file_annotations",
        "decisions", "context", "conversations", "_workspace_meta"
    };

    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        string? tableName = null;
        int limit = int.MaxValue;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--limit" when i + 1 < args.Length:
                    int.TryParse(args[++i], out limit);
                    break;
                default:
                    if (!args[i].StartsWith("--") && tableName == null)
                        tableName = args[i];
                    break;
            }
        }

        if (tableName == null)
        {
            Console.Error.WriteLine("Usage: sharc query <table> [--limit N]");
            return 1;
        }

        if (!ValidTables.Contains(tableName))
        {
            Console.Error.WriteLine($"Unknown table: {tableName}");
            Console.Error.WriteLine($"Valid tables: {string.Join(", ", ValidTables)}");
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

        try
        {
            using var reader = db.CreateReader(tableName);
            int fieldCount = reader.FieldCount;
            int rowCount = 0;

            while (reader.Read() && rowCount < limit)
            {
                var parts = new string[fieldCount];
                for (int c = 0; c < fieldCount; c++)
                {
                    parts[c] = reader.IsNull(c) ? "(null)" : reader.GetString(c);
                }
                Console.WriteLine(string.Join(" | ", parts));
                rowCount++;
            }

            if (rowCount == 0)
                Console.WriteLine($"(no rows in {tableName})");
        }
        catch
        {
            Console.Error.WriteLine($"Failed to query table: {tableName}");
            return 1;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc query <table> [--limit N]");
        Console.WriteLine();
        Console.WriteLine("Tables: commits, file_changes, notes, file_annotations,");
        Console.WriteLine("        decisions, context, conversations, _workspace_meta");
    }
}
