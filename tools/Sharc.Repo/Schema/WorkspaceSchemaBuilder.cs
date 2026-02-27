// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo.Schema;

/// <summary>
/// Creates the workspace.arc schema in a Sharc database. Idempotent — safe to call
/// on an existing database. Returns the open database for immediate use.
/// </summary>
public static class WorkspaceSchemaBuilder
{
    private static readonly string[] TableDdl =
    {
        """
        CREATE TABLE IF NOT EXISTS commits (
            id INTEGER PRIMARY KEY,
            sha TEXT NOT NULL,
            author_name TEXT NOT NULL,
            author_email TEXT NOT NULL,
            authored_at INTEGER NOT NULL,
            message TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS file_changes (
            id INTEGER PRIMARY KEY,
            commit_id INTEGER NOT NULL,
            path TEXT NOT NULL,
            lines_added INTEGER NOT NULL,
            lines_deleted INTEGER NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS notes (
            id INTEGER PRIMARY KEY,
            content TEXT NOT NULL,
            tag TEXT,
            author TEXT,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS file_annotations (
            id INTEGER PRIMARY KEY,
            file_path TEXT NOT NULL,
            annotation_type TEXT NOT NULL,
            content TEXT,
            line_start INTEGER,
            line_end INTEGER,
            author TEXT,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS decisions (
            id INTEGER PRIMARY KEY,
            decision_id TEXT NOT NULL,
            title TEXT NOT NULL,
            rationale TEXT,
            status TEXT NOT NULL,
            author TEXT,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS context (
            id INTEGER PRIMARY KEY,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            author TEXT,
            created_at INTEGER NOT NULL,
            updated_at INTEGER NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS conversations (
            id INTEGER PRIMARY KEY,
            session_id TEXT NOT NULL,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            tool_name TEXT,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS _workspace_meta (
            id INTEGER PRIMARY KEY,
            key TEXT NOT NULL,
            value TEXT NOT NULL
        )
        """,
        // ── Knowledge Graph tables ───────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS features (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            layer TEXT NOT NULL,
            status TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS feature_edges (
            id INTEGER PRIMARY KEY,
            feature_name TEXT NOT NULL,
            target_path TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            role TEXT,
            auto_detected INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS file_purposes (
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL,
            purpose TEXT NOT NULL,
            project TEXT NOT NULL,
            namespace TEXT,
            layer TEXT,
            auto_detected INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS file_deps (
            id INTEGER PRIMARY KEY,
            source_path TEXT NOT NULL,
            target_path TEXT NOT NULL,
            dep_kind TEXT NOT NULL,
            auto_detected INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL
        )
        """
    };

    // ── Knowledge Graph indexes ────────────────────────────────────
    private static readonly string[] IndexDdl =
    {
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_features_name ON features(name)",
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_file_purposes_path ON file_purposes(path)",
        "CREATE INDEX IF NOT EXISTS idx_feature_edges_feature ON feature_edges(feature_name)",
        "CREATE INDEX IF NOT EXISTS idx_feature_edges_target ON feature_edges(target_path)",
        "CREATE INDEX IF NOT EXISTS idx_file_deps_source ON file_deps(source_path)",
        "CREATE INDEX IF NOT EXISTS idx_file_deps_target ON file_deps(target_path)",
        "CREATE INDEX IF NOT EXISTS idx_file_deps_kind ON file_deps(dep_kind)",
    };

    /// <summary>
    /// Creates or opens a workspace database with the full schema.
    /// Returns the open <see cref="SharcDatabase"/> for immediate use.
    /// </summary>
    public static SharcDatabase CreateSchema(string path)
    {
        SharcDatabase db;
        if (File.Exists(path))
            db = SharcDatabase.Open(path, new SharcOpenOptions { Writable = true });
        else
            db = SharcDatabase.Create(path);

        using var tx = db.BeginTransaction();
        foreach (var ddl in TableDdl)
            tx.Execute(ddl);
        foreach (var ddl in IndexDdl)
            tx.Execute(ddl);
        tx.Commit();

        return db;
    }
}
