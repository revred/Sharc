// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Sharc.Repo.Git;

/// <summary>
/// A parsed file change from git diff --numstat output (before commit rowid is known).
/// </summary>
public readonly record struct ParsedFileChange(string Path, int LinesAdded, int LinesDeleted);

/// <summary>
/// Walks git log and diff output, parsing into structured records suitable
/// for writing to workspace.arc via <see cref="GitCommitWriter"/>.
/// </summary>
public sealed class RepoGitLogWalker
{
    private readonly string _repoPath;

    public RepoGitLogWalker(string repoPath)
    {
        _repoPath = repoPath;
    }

    /// <summary>
    /// Runs git log and returns parsed commit records.
    /// Uses unix timestamp format (%at) for direct storage as INT64.
    /// </summary>
    public async Task<List<GitCommitRecord>> GetCommitsAsync(string? afterSha = null)
    {
        var args = "log --format=\"%H|%an|%ae|%at|%s\" --reverse";
        if (!string.IsNullOrWhiteSpace(afterSha))
            args += $" {afterSha}..HEAD";

        var output = await RunGitAsync(args);
        return ParseCommitLines(output);
    }

    /// <summary>
    /// Runs git diff --numstat for a specific commit and returns file changes.
    /// </summary>
    public async Task<List<ParsedFileChange>> GetFileChangesAsync(string commitSha)
    {
        // Use diff-tree with --root to handle the very first commit
        var output = await RunGitAsync($"diff-tree --numstat --root -r {commitSha}");
        return ParseDiffStatLines(output);
    }

    /// <summary>
    /// Parses a single line of git log output in "%H|%an|%ae|%at|%s" format.
    /// </summary>
    public static GitCommitRecord? ParseCommitLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('|', 5);
        if (parts.Length < 5)
            return null;

        if (!long.TryParse(parts[3].Trim(), out long timestamp))
            return null;

        return new GitCommitRecord(
            Sha: parts[0].Trim(),
            AuthorName: parts[1].Trim(),
            AuthorEmail: parts[2].Trim(),
            AuthoredAt: timestamp,
            Message: parts[4].Trim());
    }

    /// <summary>
    /// Parses multiple lines of git log output.
    /// </summary>
    public static List<GitCommitRecord> ParseCommitLines(string output)
    {
        var commits = new List<GitCommitRecord>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var commit = ParseCommitLine(line);
            if (commit != null)
                commits.Add(commit);
        }
        return commits;
    }

    /// <summary>
    /// Parses a single line of git diff --numstat output.
    /// Format: "added\tdeleted\tpath". Binary files show "-\t-\tpath".
    /// </summary>
    public static ParsedFileChange? ParseDiffStatLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('\t', 3);
        if (parts.Length < 3)
            return null;

        int added = 0, deleted = 0;
        if (parts[0] != "-")
            int.TryParse(parts[0], out added);
        if (parts[1] != "-")
            int.TryParse(parts[1], out deleted);

        var path = parts[2].Trim();
        if (string.IsNullOrEmpty(path))
            return null;

        return new ParsedFileChange(path, added, deleted);
    }

    /// <summary>
    /// Parses multiple lines of git diff --numstat output.
    /// </summary>
    public static List<ParsedFileChange> ParseDiffStatLines(string output)
    {
        var changes = new List<ParsedFileChange>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var change = ParseDiffStatLine(line);
            if (change != null)
                changes.Add(change.Value);
        }
        return changes;
    }

    private async Task<string> RunGitAsync(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }
}
