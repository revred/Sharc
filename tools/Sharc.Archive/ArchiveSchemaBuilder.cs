// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Archive;

/// <summary>
/// Creates the archive schema in a Sharc database. Idempotent — safe to call
/// on an existing database. Returns the open database for immediate use.
/// </summary>
public static class ArchiveSchemaBuilder
{
    private static readonly string[] TableDdl =
    {
        """
        CREATE TABLE IF NOT EXISTS conversations (
            id INTEGER PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            title TEXT,
            started_at INTEGER NOT NULL,
            ended_at INTEGER,
            agent_id TEXT,
            source TEXT,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS turns (
            id INTEGER PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            turn_index INTEGER NOT NULL,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            token_count INTEGER,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS annotations (
            id INTEGER PRIMARY KEY,
            target_type TEXT NOT NULL,
            target_id INTEGER NOT NULL,
            annotation_type TEXT NOT NULL,
            verdict TEXT,
            content TEXT,
            annotator_id TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS file_annotations (
            id INTEGER PRIMARY KEY,
            turn_id INTEGER NOT NULL,
            file_path TEXT NOT NULL,
            annotation_type TEXT NOT NULL,
            content TEXT,
            line_start INTEGER,
            line_end INTEGER,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS decisions (
            id INTEGER PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            turn_id INTEGER,
            decision_id TEXT NOT NULL,
            title TEXT NOT NULL,
            rationale TEXT,
            status TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS checkpoints (
            id INTEGER PRIMARY KEY,
            checkpoint_id TEXT NOT NULL,
            label TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            conversation_count INTEGER NOT NULL,
            turn_count INTEGER NOT NULL,
            annotation_count INTEGER NOT NULL,
            ledger_sequence INTEGER NOT NULL,
            metadata BLOB
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS _sharc_manifest (
            id INTEGER PRIMARY KEY,
            fragment_id TEXT NOT NULL,
            version INTEGER NOT NULL,
            source_uri TEXT,
            last_sync_at INTEGER NOT NULL,
            entry_count INTEGER NOT NULL,
            ledger_sequence INTEGER NOT NULL,
            checksum BLOB,
            metadata BLOB
        )
        """
    };

    /// <summary>
    /// Creates or opens an archive database with the full schema.
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
        tx.Commit();

        return db;
    }
}
