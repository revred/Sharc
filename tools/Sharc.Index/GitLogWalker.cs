// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Sharc.Index;

/// <summary>
/// A parsed commit from git log output.
/// </summary>
public sealed record CommitRecord(
    string Sha,
    string AuthorName,
    string AuthorEmail,
    string AuthoredDate,
    string Message);

/// <summary>
/// A single file change from git diff --numstat output.
/// </summary>
public sealed record FileChangeRecord(
    string CommitSha,
    string Path,
    int LinesAdded,
    int LinesDeleted);

/// <summary>
/// Walks git log and diff output, parsing into structured records.
/// </summary>
public sealed class GitLogWalker
{
    private readonly string _repoPath;

    public GitLogWalker(string repoPath)
    {
        _repoPath = repoPath;
    }

    /// <summary>
    /// Runs git log and returns parsed commit records.
    /// </summary>
    public async Task<List<CommitRecord>> GetCommitsAsync(string? since = null)
    {
        var args = "log --format=\"%H|%an|%ae|%aI|%s\"";
        if (!string.IsNullOrWhiteSpace(since))
            args += $" --since=\"{since}\"";

        var output = await RunGitAsync(args);
        var commits = new List<CommitRecord>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var commit = ParseCommitLine(line);
            if (commit != null)
                commits.Add(commit);
        }
        return commits;
    }

    /// <summary>
    /// Runs git diff --numstat for a specific commit and returns file changes.
    /// </summary>
    public async Task<List<FileChangeRecord>> GetFileChangesAsync(string commitSha)
    {
        var output = await RunGitAsync($"diff --numstat {commitSha}~1 {commitSha}");
        var changes = new List<FileChangeRecord>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var change = ParseDiffStatLine(commitSha, line);
            if (change != null)
                changes.Add(change);
        }
        return changes;
    }

    /// <summary>
    /// Parses a single line of git log output in "%H|%an|%ae|%aI|%s" format.
    /// </summary>
    public static CommitRecord? ParseCommitLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('|', 5);
        if (parts.Length < 5)
            return null;

        return new CommitRecord(
            Sha: parts[0].Trim(),
            AuthorName: parts[1].Trim(),
            AuthorEmail: parts[2].Trim(),
            AuthoredDate: parts[3].Trim(),
            Message: parts[4].Trim());
    }

    /// <summary>
    /// Parses a single line of git diff --numstat output.
    /// Format: "added\tdeleted\tpath". Binary files show "-\t-\tpath".
    /// </summary>
    public static FileChangeRecord? ParseDiffStatLine(string commitSha, string line)
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

        return new FileChangeRecord(commitSha, path, added, deleted);
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
