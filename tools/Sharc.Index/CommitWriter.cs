// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Sharc.Index;

/// <summary>
/// Writes parsed commit and file change data into the GCD SQLite database.
/// </summary>
public sealed class CommitWriter : IDisposable
{
    private readonly SqliteConnection _conn;

    public CommitWriter(string databasePath)
    {
        _conn = new SqliteConnection($"Data Source={databasePath}");
        _conn.Open();
    }

    /// <summary>
    /// Inserts commit records into the commits table. Duplicates are ignored.
    /// </summary>
    public void WriteCommits(IReadOnlyList<CommitRecord> commits)
    {
        if (commits.Count == 0)
            return;

        using var transaction = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO commits (sha, author_name, author_email, authored_date, message)
            VALUES ($sha, $name, $email, $date, $msg)
            """;
        var pSha = cmd.Parameters.Add("$sha", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pEmail = cmd.Parameters.Add("$email", SqliteType.Text);
        var pDate = cmd.Parameters.Add("$date", SqliteType.Text);
        var pMsg = cmd.Parameters.Add("$msg", SqliteType.Text);

        foreach (var commit in commits)
        {
            pSha.Value = commit.Sha;
            pName.Value = commit.AuthorName;
            pEmail.Value = commit.AuthorEmail;
            pDate.Value = commit.AuthoredDate;
            pMsg.Value = commit.Message;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>
    /// Inserts file change records and upserts the files table.
    /// </summary>
    public void WriteFileChanges(IReadOnlyList<FileChangeRecord> changes)
    {
        if (changes.Count == 0)
            return;

        using var transaction = _conn.BeginTransaction();

        // Upsert files table
        using (var fileCmd = _conn.CreateCommand())
        {
            fileCmd.CommandText = """
                INSERT INTO files (path, first_seen_sha, last_modified_sha)
                VALUES ($path, $sha, $sha)
                ON CONFLICT(path) DO UPDATE SET last_modified_sha = $sha
                """;
            var pPath = fileCmd.Parameters.Add("$path", SqliteType.Text);
            var pSha = fileCmd.Parameters.Add("$sha", SqliteType.Text);

            foreach (var change in changes)
            {
                pPath.Value = change.Path;
                pSha.Value = change.CommitSha;
                fileCmd.ExecuteNonQuery();
            }
        }

        // Insert file_changes
        using (var changeCmd = _conn.CreateCommand())
        {
            changeCmd.CommandText = """
                INSERT OR IGNORE INTO file_changes (commit_sha, path, lines_added, lines_deleted)
                VALUES ($sha, $path, $added, $deleted)
                """;
            var pSha = changeCmd.Parameters.Add("$sha", SqliteType.Text);
            var pPath = changeCmd.Parameters.Add("$path", SqliteType.Text);
            var pAdded = changeCmd.Parameters.Add("$added", SqliteType.Integer);
            var pDeleted = changeCmd.Parameters.Add("$deleted", SqliteType.Integer);

            foreach (var change in changes)
            {
                pSha.Value = change.CommitSha;
                pPath.Value = change.Path;
                pAdded.Value = change.LinesAdded;
                pDeleted.Value = change.LinesDeleted;
                changeCmd.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
