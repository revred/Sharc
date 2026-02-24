// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Records an architectural decision in workspace.arc.
/// </summary>
public static class DecideCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        string? decisionId = null;
        string? title = null;
        string? rationale = null;
        string status = "accepted";
        string? author = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rationale" when i + 1 < args.Length: rationale = args[++i]; break;
                case "--status" when i + 1 < args.Length: status = args[++i]; break;
                case "--author" when i + 1 < args.Length: author = args[++i]; break;
                default:
                    if (!args[i].StartsWith("--"))
                    {
                        if (decisionId == null) decisionId = args[i];
                        else if (title == null) title = args[i];
                    }
                    break;
            }
        }

        if (decisionId == null || title == null)
        {
            Console.Error.WriteLine("Usage: sharc decide <id> \"title\" [--rationale r] [--status s] [--author a]");
            return 1;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        if (!ChannelGuard.IsEnabled("decisions"))
            return 1;

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteDecision(new DecisionRecord(
            decisionId, title, rationale, status, author, now, null));

        Console.WriteLine($"Decision {decisionId} recorded.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc decide <id> \"title\" [--rationale r] [--status s] [--author a]");
    }
}
