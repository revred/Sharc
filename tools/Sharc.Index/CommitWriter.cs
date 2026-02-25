// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;

namespace Sharc.Index;

/// <summary>
/// Writes parsed commit and file change data into the GCD Sharc database.
/// Migrated from Microsoft.Data.Sqlite to SharcWriter (E-2).
/// Uses scan-before-insert for SHA deduplication (no UNIQUE constraints in SharcWriter DDL).
/// </summary>
public sealed class CommitWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly HashSet<string> _knownShas;
    private readonly Dictionary<string, (long RowId, string FirstSeenSha)> _knownFiles;

    public CommitWriter(SharcDatabase db)
    {
        _db = db;
        _knownShas = LoadExistingShas();
        _knownFiles = LoadExistingFiles();
    }

    /// <summary>
    /// Inserts commit records into the commits table. Duplicates (by SHA) are skipped.
    /// </summary>
    public void WriteCommits(IReadOnlyList<CommitRecord> commits)
    {
        if (commits.Count == 0)
            return;

        using var sw = SharcWriter.From(_db);
        foreach (var commit in commits)
        {
            if (_knownShas.Contains(commit.Sha))
                continue;

            sw.Insert("commits",
                TextVal(commit.Sha),
                TextVal(commit.AuthorName),
                TextVal(commit.AuthorEmail),
                TextVal(commit.AuthoredDate),
                TextVal(commit.Message));
            _knownShas.Add(commit.Sha);
        }
    }

    /// <summary>
    /// Inserts file change records and upserts the files table.
    /// For files table: scan-then-update for existing paths, insert for new paths.
    /// </summary>
    public void WriteFileChanges(IReadOnlyList<FileChangeRecord> changes)
    {
        if (changes.Count == 0)
            return;

        using var sw = SharcWriter.From(_db);
        foreach (var change in changes)
        {
            // Upsert files table (preserve first_seen_sha on update)
            if (_knownFiles.TryGetValue(change.Path, out var existing))
            {
                // Update: keep original first_seen_sha, update last_modified_sha
                sw.Update("files", existing.RowId,
                    TextVal(change.Path),
                    TextVal(existing.FirstSeenSha),
                    TextVal(change.CommitSha));
                _knownFiles[change.Path] = (existing.RowId, existing.FirstSeenSha);
            }
            else
            {
                sw.Insert("files",
                    TextVal(change.Path),
                    TextVal(change.CommitSha),  // first_seen_sha
                    TextVal(change.CommitSha)); // last_modified_sha
            }

            // Insert file change
            sw.Insert("file_changes",
                TextVal(change.CommitSha),
                TextVal(change.Path),
                ColumnValue.FromInt64(1, change.LinesAdded),
                ColumnValue.FromInt64(1, change.LinesDeleted));
        }

        // Reload file index to pick up new rowIds
        _knownFiles.Clear();
        foreach (var kvp in LoadExistingFiles())
            _knownFiles[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Inserts or updates author records. Deduplicates by email (scan-before-insert).
    /// </summary>
    public void WriteAuthors(IReadOnlyList<AuthorRecord> authors)
    {
        if (authors.Count == 0)
            return;

        // Build lookup of existing authors by email → rowId
        var existing = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = _db.CreateReader("authors");
            while (reader.Read())
                existing[reader.GetString(1)] = reader.RowId; // ordinal 1 = email
        }
        catch { /* table may be empty */ }

        using var sw = SharcWriter.From(_db);
        foreach (var author in authors)
        {
            if (existing.TryGetValue(author.Email, out var rowId))
            {
                sw.Update("authors", rowId,
                    TextVal(author.Name),
                    TextVal(author.Email),
                    TextVal(author.FirstCommitSha),
                    ColumnValue.FromInt64(1, author.CommitCount));
            }
            else
            {
                sw.Insert("authors",
                    TextVal(author.Name),
                    TextVal(author.Email),
                    TextVal(author.FirstCommitSha),
                    ColumnValue.FromInt64(1, author.CommitCount));
                // We don't need to track the new rowId since scan is cheap
            }
        }
    }

    /// <summary>
    /// Inserts commit parent records (supports merge commits with multiple parents).
    /// </summary>
    public void WriteCommitParents(IReadOnlyList<CommitParentRecord> parents)
    {
        if (parents.Count == 0)
            return;

        using var sw = SharcWriter.From(_db);
        foreach (var parent in parents)
        {
            sw.Insert("commit_parents",
                TextVal(parent.CommitSha),
                TextVal(parent.ParentSha),
                ColumnValue.FromInt64(1, parent.Ordinal));
        }
    }

    /// <summary>
    /// Inserts or updates branch records. Deduplicates by name (scan-before-insert).
    /// </summary>
    public void WriteBranches(IReadOnlyList<BranchRecord> branches)
    {
        if (branches.Count == 0)
            return;

        var existing = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            using var reader = _db.CreateReader("branches");
            while (reader.Read())
                existing[reader.GetString(0)] = reader.RowId; // ordinal 0 = name
        }
        catch { /* table may be empty */ }

        using var sw = SharcWriter.From(_db);
        foreach (var branch in branches)
        {
            if (existing.TryGetValue(branch.Name, out var rowId))
            {
                sw.Update("branches", rowId,
                    TextVal(branch.Name),
                    TextVal(branch.HeadSha),
                    ColumnValue.FromInt64(1, branch.IsRemote),
                    ColumnValue.FromInt64(1, branch.UpdatedAt));
            }
            else
            {
                sw.Insert("branches",
                    TextVal(branch.Name),
                    TextVal(branch.HeadSha),
                    ColumnValue.FromInt64(1, branch.IsRemote),
                    ColumnValue.FromInt64(1, branch.UpdatedAt));
            }
        }
    }

    /// <summary>
    /// Inserts tag records (annotated and lightweight).
    /// </summary>
    public void WriteTags(IReadOnlyList<TagRecord> tags)
    {
        if (tags.Count == 0)
            return;

        using var sw = SharcWriter.From(_db);
        foreach (var tag in tags)
        {
            sw.Insert("tags",
                TextVal(tag.Name),
                TextVal(tag.TargetSha),
                NullableTextVal(tag.TaggerName),
                NullableTextVal(tag.TaggerEmail),
                NullableTextVal(tag.Message),
                ColumnValue.FromInt64(1, tag.CreatedAt));
        }
    }

    /// <summary>
    /// Sets a key-value pair in the _index_state table. Upserts by key.
    /// Used for incremental indexing (E-3: last_indexed_sha, etc.).
    /// </summary>
    public void SetIndexState(string key, string value)
    {
        // Scan for existing key
        long? existingRowId = null;
        try
        {
            using var reader = _db.CreateReader("_index_state");
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                {
                    existingRowId = reader.RowId;
                    break;
                }
            }
        }
        catch { /* table may be empty */ }

        using var sw = SharcWriter.From(_db);
        if (existingRowId.HasValue)
        {
            sw.Update("_index_state", existingRowId.Value,
                TextVal(key),
                TextVal(value));
        }
        else
        {
            sw.Insert("_index_state",
                TextVal(key),
                TextVal(value));
        }
    }

    /// <summary>
    /// Gets a value from the _index_state table by key. Returns null if not found.
    /// </summary>
    public string? GetIndexState(string key)
    {
        try
        {
            using var reader = _db.CreateReader("_index_state");
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                    return reader.GetString(1);
            }
        }
        catch { /* table may be empty */ }
        return null;
    }

    private HashSet<string> LoadExistingShas()
    {
        var shas = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            // Full scan — ordinal 0 = sha (first non-id column in B-tree record)
            using var reader = _db.CreateReader("commits");
            while (reader.Read())
                shas.Add(reader.GetString(0));
        }
        catch { /* table may not exist yet */ }
        return shas;
    }

    private Dictionary<string, (long RowId, string FirstSeenSha)> LoadExistingFiles()
    {
        var files = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        try
        {
            // Full scan — ordinal 0 = path, ordinal 1 = first_seen_sha
            using var reader = _db.CreateReader("files");
            while (reader.Read())
                files[reader.GetString(0)] = (reader.RowId, reader.GetString(1));
        }
        catch { /* table may not exist yet */ }
        return files;
    }

    private static ColumnValue TextVal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    private static ColumnValue NullableTextVal(string? value)
    {
        if (value is null)
            return ColumnValue.Null();
        return TextVal(value);
    }

    public void Dispose()
    {
        // Database is not owned by this writer — caller manages its lifetime
    }
}
