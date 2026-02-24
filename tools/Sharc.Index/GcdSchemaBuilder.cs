// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;

namespace Sharc.Index;

/// <summary>
/// Creates the GitHub Context Database (GCD) schema in a Sharc .arc file.
/// Migrated from Microsoft.Data.Sqlite to SharcDatabase (E-2).
/// </summary>
public static class GcdSchemaBuilder
{
    /// <summary>
    /// Creates the core GCD tables (commits, files, file_changes) in the specified database.
    /// Uses IF NOT EXISTS for idempotency. Creates the file if it doesn't exist.
    /// </summary>
    public static SharcDatabase CreateSchema(string databasePath)
    {
        var db = SharcDatabase.Create(databasePath);
        using var tx = db.BeginTransaction();

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS commits (
                id INTEGER PRIMARY KEY,
                sha TEXT NOT NULL,
                author_name TEXT NOT NULL,
                author_email TEXT NOT NULL,
                authored_date TEXT NOT NULL,
                message TEXT NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL,
                first_seen_sha TEXT NOT NULL,
                last_modified_sha TEXT NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS file_changes (
                id INTEGER PRIMARY KEY,
                commit_sha TEXT NOT NULL,
                path TEXT NOT NULL,
                lines_added INTEGER NOT NULL DEFAULT 0,
                lines_deleted INTEGER NOT NULL DEFAULT 0
            )
            """);

        // E-1: Enhanced git schema — 7 new tables

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS authors (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                first_commit_sha TEXT NOT NULL,
                commit_count INTEGER NOT NULL DEFAULT 1
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS commit_parents (
                id INTEGER PRIMARY KEY,
                commit_sha TEXT NOT NULL,
                parent_sha TEXT NOT NULL,
                ordinal INTEGER NOT NULL DEFAULT 0
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS branches (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                head_sha TEXT NOT NULL,
                is_remote INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                target_sha TEXT NOT NULL,
                tagger_name TEXT,
                tagger_email TEXT,
                message TEXT,
                created_at INTEGER NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS diff_hunks (
                id INTEGER PRIMARY KEY,
                commit_sha TEXT NOT NULL,
                path TEXT NOT NULL,
                old_start INTEGER NOT NULL,
                old_lines INTEGER NOT NULL,
                new_start INTEGER NOT NULL,
                new_lines INTEGER NOT NULL,
                content TEXT NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS blame_lines (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                commit_sha TEXT NOT NULL,
                author_name TEXT NOT NULL,
                author_email TEXT NOT NULL,
                line_content TEXT NOT NULL
            )
            """);

        tx.Execute("""
            CREATE TABLE IF NOT EXISTS _index_state (
                id INTEGER PRIMARY KEY,
                key TEXT NOT NULL,
                value TEXT NOT NULL
            )
            """);

        tx.Commit();
        return db;
    }
}
