// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Adds a free-form note to workspace.arc.
/// </summary>
public static class NoteCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        string? content = null;
        string? tag = null;
        string? author = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tag" when i + 1 < args.Length: tag = args[++i]; break;
                case "--author" when i + 1 < args.Length: author = args[++i]; break;
                default:
                    if (content == null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        content = args[i];
                    break;
            }
        }

        if (content == null)
        {
            Console.Error.WriteLine("Usage: sharc note \"content\" [--tag t] [--author a]");
            return 1;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        if (!ChannelGuard.IsEnabled("notes"))
            return 1;

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteNote(new NoteRecord(content, tag, author, now, null));

        Console.WriteLine("Note added.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc note \"content\" [--tag t] [--author a]");
    }
}
