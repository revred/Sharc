// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Annotates a file in workspace.arc with a type (note/todo/bug/review/important).
/// </summary>
public static class AnnotateCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        string? filePath = null;
        string? content = null;
        string type = "note";
        string? lineSpec = null;
        string? author = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type" when i + 1 < args.Length: type = args[++i]; break;
                case "--line" when i + 1 < args.Length: lineSpec = args[++i]; break;
                case "--author" when i + 1 < args.Length: author = args[++i]; break;
                default:
                    if (!args[i].StartsWith("--"))
                    {
                        if (filePath == null) filePath = args[i];
                        else if (content == null) content = args[i];
                    }
                    break;
            }
        }

        if (filePath == null || content == null)
        {
            Console.Error.WriteLine("Usage: sharc annotate <file> \"content\" [--type t] [--line s[-e]] [--author a]");
            return 1;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        if (!ChannelGuard.IsEnabled("annotations"))
            return 1;

        int? lineStart = null, lineEnd = null;
        if (lineSpec != null)
        {
            var parts = lineSpec.Split('-', 2);
            if (int.TryParse(parts[0], out int s))
            {
                lineStart = s;
                lineEnd = parts.Length > 1 && int.TryParse(parts[1], out int e) ? e : s;
            }
        }

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteFileAnnotation(new FileAnnotationRecord(
            filePath, type, content, lineStart, lineEnd, author, now, null));

        Console.WriteLine($"Annotated {filePath}.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc annotate <file> \"content\" [--type t] [--line s[-e]] [--author a]");
    }
}
