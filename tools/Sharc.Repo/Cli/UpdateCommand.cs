// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Repo.Data;
using Sharc.Repo.Git;

namespace Sharc.Repo.Cli;

/// <summary>
/// Indexes git commits and file changes into workspace.arc.
/// Supports incremental updates via _workspace_meta.last_indexed_sha.
/// </summary>
public static class UpdateCommand
{
    public static int Run(string[] args)
    {
        bool full = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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

        var gitRoot = RepoLocator.FindGitRoot()!;
        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);

        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });

        // Determine starting point for incremental indexing
        string? afterSha = null;
        if (!full)
        {
            var reader = new WorkspaceReader(db);
            afterSha = reader.GetMeta("last_indexed_sha");
        }

        // Walk git log
        var walker = new RepoGitLogWalker(gitRoot);
        var commits = walker.GetCommitsAsync(afterSha).GetAwaiter().GetResult();

        if (commits.Count == 0)
        {
            Console.WriteLine("No new commits to index.");
            return 0;
        }

        // Write commits and file changes
        using var gcw = new GitCommitWriter(db);
        var written = gcw.WriteCommits(commits);

        int fileChangeCount = 0;
        foreach (var (rowId, sha) in written)
        {
            var changes = walker.GetFileChangesAsync(sha).GetAwaiter().GetResult();
            gcw.WriteFileChanges(rowId, changes);
            fileChangeCount += changes.Count;
        }

        // Update last_indexed_sha metadata
        if (written.Count > 0)
        {
            var lastSha = written[^1].Sha;
            UpdateMeta(db, "last_indexed_sha", lastSha);
        }

        Console.WriteLine($"Indexed {written.Count} commits, {fileChangeCount} file changes.");
        return 0;
    }

    private static void UpdateMeta(SharcDatabase db, string key, string value)
    {
        // Scan-then-update pattern for _workspace_meta
        long? existingRowId = null;
        using (var reader = db.CreateReader("_workspace_meta"))
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                {
                    existingRowId = reader.RowId;
                    break;
                }
            }
        }

        if (existingRowId.HasValue)
        {
            var kBytes = System.Text.Encoding.UTF8.GetBytes(key);
            var vBytes = System.Text.Encoding.UTF8.GetBytes(value);
            using var sw = SharcWriter.From(db);
            sw.Update("_workspace_meta", existingRowId.Value,
                ColumnValue.Text(2 * kBytes.Length + 13, kBytes),
                ColumnValue.Text(2 * vBytes.Length + 13, vBytes));
        }
        else
        {
            using var w = new WorkspaceWriter(db);
            w.WriteMeta(key, value);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc update [--since <date>] [--full]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --full   Re-index all commits (ignore last_indexed_sha)");
        Console.WriteLine("  --help   Show this help message");
    }
}
