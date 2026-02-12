using Microsoft.Data.Sqlite;

namespace Sharc.Index;

/// <summary>
/// Creates the GitHub Context Database (GCD) schema in a SQLite file.
/// </summary>
public static class GcdSchemaBuilder
{
    /// <summary>
    /// Creates the core GCD tables (commits, files, file_changes) in the specified database.
    /// Uses IF NOT EXISTS for idempotency.
    /// </summary>
    public static void CreateSchema(string databasePath)
    {
        using var conn = new SqliteConnection($"Data Source={databasePath}");
        conn.Open();

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS commits (
                sha TEXT NOT NULL PRIMARY KEY,
                author_name TEXT NOT NULL,
                author_email TEXT NOT NULL,
                authored_date TEXT NOT NULL,
                message TEXT NOT NULL
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS files (
                path TEXT NOT NULL PRIMARY KEY,
                first_seen_sha TEXT NOT NULL,
                last_modified_sha TEXT NOT NULL,
                FOREIGN KEY (first_seen_sha) REFERENCES commits(sha),
                FOREIGN KEY (last_modified_sha) REFERENCES commits(sha)
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS file_changes (
                commit_sha TEXT NOT NULL,
                path TEXT NOT NULL,
                lines_added INTEGER NOT NULL DEFAULT 0,
                lines_deleted INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (commit_sha, path),
                FOREIGN KEY (commit_sha) REFERENCES commits(sha),
                FOREIGN KEY (path) REFERENCES files(path)
            )
            """);

        Execute(conn, "PRAGMA journal_mode=WAL");
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
