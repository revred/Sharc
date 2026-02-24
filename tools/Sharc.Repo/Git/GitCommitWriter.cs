// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Git;

/// <summary>
/// Writes git commit and file change records to workspace.arc,
/// with deduplication by SHA.
/// </summary>
public sealed class GitCommitWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly WorkspaceWriter _writer;
    private readonly HashSet<string> _existingShas;
    private bool _disposed;

    public GitCommitWriter(SharcDatabase db)
    {
        _db = db;
        _writer = new WorkspaceWriter(db);
        _existingShas = LoadExistingShas();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Writes commits to workspace.arc, skipping any with a SHA that already exists.
    /// Returns the list of (rowId, sha) pairs for commits that were actually written.
    /// </summary>
    public IReadOnlyList<(long RowId, string Sha)> WriteCommits(IReadOnlyList<GitCommitRecord> commits)
    {
        var written = new List<(long, string)>();
        foreach (var commit in commits)
        {
            if (_existingShas.Contains(commit.Sha))
                continue;

            long rowId = _writer.WriteCommit(commit);
            _existingShas.Add(commit.Sha);
            written.Add((rowId, commit.Sha));
        }
        return written;
    }

    /// <summary>
    /// Writes file changes for a specific commit rowid.
    /// </summary>
    public void WriteFileChanges(long commitRowId, IReadOnlyList<ParsedFileChange> changes)
    {
        foreach (var change in changes)
        {
            _writer.WriteFileChange(new GitFileChangeRecord(
                commitRowId, change.Path, change.LinesAdded, change.LinesDeleted));
        }
    }

    /// <summary>
    /// Returns the set of all commit SHAs already in the database.
    /// </summary>
    public HashSet<string> GetExistingShas() => new(_existingShas);

    private HashSet<string> LoadExistingShas()
    {
        var shas = new HashSet<string>(StringComparer.Ordinal);
        using var reader = _db.CreateReader("commits");
        while (reader.Read())
        {
            shas.Add(reader.GetString(0)); // sha is ordinal 0 (after rowid alias)
        }
        return shas;
    }
}
