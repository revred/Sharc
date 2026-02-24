// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Schema;

namespace Sharc.Repo.Cli;

/// <summary>
/// Creates the .sharc/ folder at the git repo root with workspace.arc and config.arc.
/// </summary>
public static class InitCommand
{
    public static int Run(string[] args)
    {
        bool track = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--track": track = true; break;
                case "--help": PrintHelp(); return 0;
            }
        }

        var gitRoot = RepoLocator.FindGitRoot();
        if (gitRoot == null)
        {
            Console.Error.WriteLine("Error: Not inside a git repository.");
            return 1;
        }

        var sharcDir = Path.Combine(gitRoot, RepoLocator.SharcDirName);
        if (Directory.Exists(sharcDir))
        {
            Console.Error.WriteLine($"Already initialized: {sharcDir}");
            return 1;
        }

        Directory.CreateDirectory(sharcDir);

        // Create workspace.arc with full schema
        var workspacePath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using (var db = WorkspaceSchemaBuilder.CreateSchema(workspacePath))
        {
            // Write initial meta
            using var writer = new Data.WorkspaceWriter(db);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteMeta("version", "1");
            writer.WriteMeta("created_at", now.ToString());
            writer.WriteMeta("git_root", gitRoot);
        }

        // Create config.arc with defaults
        var configPath = Path.Combine(sharcDir, RepoLocator.ConfigFileName);
        using (var db = ConfigSchemaBuilder.CreateSchema(configPath))
        {
            // defaults are seeded by ConfigSchemaBuilder
        }

        // Add .sharc/ to .gitignore unless --track
        if (!track)
            EnsureGitignore(gitRoot);

        Console.WriteLine($"Initialized .sharc/ at {sharcDir}");
        return 0;
    }

    private static void EnsureGitignore(string gitRoot)
    {
        var gitignorePath = Path.Combine(gitRoot, ".gitignore");
        var entry = ".sharc/";

        if (File.Exists(gitignorePath))
        {
            var content = File.ReadAllText(gitignorePath);
            if (content.Contains(entry, StringComparison.Ordinal))
                return;
            File.AppendAllText(gitignorePath, Environment.NewLine + entry + Environment.NewLine);
        }
        else
        {
            File.WriteAllText(gitignorePath, entry + Environment.NewLine);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc init [--encrypt] [--track]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --track    Do not add .sharc/ to .gitignore");
        Console.WriteLine("  --encrypt  Create encrypted workspace (prompts for password)");
    }
}
