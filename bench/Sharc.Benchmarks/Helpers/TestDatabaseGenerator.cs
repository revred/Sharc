/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Microsoft.Data.Sqlite;

namespace Sharc.Benchmarks.Helpers;

/// <summary>
/// Generates the canonical benchmark SQLite database per BenchmarkSpec.md.
/// Tables: config (100 rows), users (10K rows), events (100K rows), reports (1K rows).
/// Uses Pooling=false to ensure files are fully released after creation.
/// </summary>
internal static class TestDatabaseGenerator
{
    private static readonly string[] FirstNames =
        ["alice", "bob", "carol", "dave", "eve", "frank", "grace", "heidi", "ivan", "judy"];

    private static readonly string[] Domains =
        ["example.com", "test.org", "mail.io", "corp.dev", "acme.co"];

    /// <summary>
    /// Creates the canonical benchmark database with all four tables + indexes.
    /// </summary>
    public static string CreateCanonicalDatabase(string directory)
    {
        var path = Path.Combine(directory, "bench_canonical.db");
        if (File.Exists(path)) File.Delete(path);

        using var conn = new SqliteConnection($"Data Source={path};Pooling=false");
        conn.Open();

        Execute(conn, """
            PRAGMA journal_mode=DELETE;
            PRAGMA page_size=4096;

            CREATE TABLE config (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE users (
                id         INTEGER PRIMARY KEY,
                username   TEXT NOT NULL,
                email      TEXT NOT NULL,
                bio        TEXT,
                age        INTEGER NOT NULL,
                balance    REAL NOT NULL,
                avatar     BLOB,
                is_active  INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE events (
                id         INTEGER PRIMARY KEY,
                user_id    INTEGER NOT NULL,
                event_type INTEGER NOT NULL,
                timestamp  INTEGER NOT NULL,
                value      REAL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE reports (
                id      INTEGER PRIMARY KEY,
                label   TEXT NOT NULL,
                col_01  REAL, col_02  REAL, col_03  REAL, col_04  REAL, col_05  REAL,
                col_06  REAL, col_07  REAL, col_08  REAL, col_09  REAL, col_10  REAL,
                col_11  REAL, col_12  REAL, col_13  REAL, col_14  REAL, col_15  REAL,
                col_16  REAL, col_17  REAL, col_18  REAL, col_19  REAL, col_20  REAL,
                notes   TEXT
            );

            CREATE INDEX idx_users_username ON users(username);
            CREATE INDEX idx_users_email ON users(email);
            CREATE INDEX idx_events_user_id ON events(user_id);
            CREATE INDEX idx_events_timestamp ON events(timestamp);
            CREATE INDEX idx_events_user_time ON events(user_id, timestamp);
        """);

        var rng = new Random(42);

        InsertConfig(conn, rng);
        InsertUsers(conn, rng);
        InsertEvents(conn, rng);
        InsertReports(conn, rng);

        return path;
    }

    private static void InsertConfig(SqliteConnection conn, Random rng)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO config (key, value) VALUES (@k, @v)";
        var pk = cmd.Parameters.Add("@k", SqliteType.Text);
        var pv = cmd.Parameters.Add("@v", SqliteType.Text);

        for (int i = 0; i < 100; i++)
        {
            pk.Value = $"config_key_{i:D3}";
            pv.Value = $"value_{rng.Next(1000, 9999)}_{new string('x', rng.Next(5, 50))}";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void InsertUsers(SqliteConnection conn, Random rng)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO users (username, email, bio, age, balance, avatar, is_active, created_at)
            VALUES (@u, @e, @bio, @age, @bal, @av, @act, @ts)
        """;
        var pUser = cmd.Parameters.Add("@u", SqliteType.Text);
        var pEmail = cmd.Parameters.Add("@e", SqliteType.Text);
        var pBio = cmd.Parameters.Add("@bio", SqliteType.Text);
        var pAge = cmd.Parameters.Add("@age", SqliteType.Integer);
        var pBal = cmd.Parameters.Add("@bal", SqliteType.Real);
        var pAvatar = cmd.Parameters.Add("@av", SqliteType.Blob);
        var pActive = cmd.Parameters.Add("@act", SqliteType.Integer);
        var pTs = cmd.Parameters.Add("@ts", SqliteType.Text);

        var baseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 10_000; i++)
        {
            string name = $"{FirstNames[rng.Next(FirstNames.Length)]}_{i:D5}";
            pUser.Value = name;
            pEmail.Value = $"{name}@{Domains[rng.Next(Domains.Length)]}";

            pBio.Value = rng.NextDouble() < 0.3
                ? DBNull.Value
                : new string('A', rng.Next(50, 500));

            pAge.Value = 18 + rng.Next(73);
            pBal.Value = Math.Round(rng.NextDouble() * 99999.99, 2);

            if (rng.NextDouble() < 0.5)
                pAvatar.Value = DBNull.Value;
            else
            {
                var blob = new byte[rng.Next(1024, 4096)];
                rng.NextBytes(blob);
                pAvatar.Value = blob;
            }

            pActive.Value = rng.Next(2);
            pTs.Value = baseDate.AddSeconds(rng.Next(0, 157_680_000)).ToString("O");
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void InsertEvents(SqliteConnection conn, Random rng)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO events (user_id, event_type, timestamp, value)
            VALUES (@uid, @et, @ts, @val)
        """;
        var pUid = cmd.Parameters.Add("@uid", SqliteType.Integer);
        var pType = cmd.Parameters.Add("@et", SqliteType.Integer);
        var pTs = cmd.Parameters.Add("@ts", SqliteType.Integer);
        var pVal = cmd.Parameters.Add("@val", SqliteType.Real);

        long baseEpoch = 1577836800L;

        for (int i = 0; i < 100_000; i++)
        {
            pUid.Value = rng.Next(1, 10_001);
            pType.Value = rng.Next(1, 51);
            pTs.Value = baseEpoch + rng.Next(0, 157_680_000);

            pVal.Value = rng.NextDouble() < 0.2
                ? DBNull.Value
                : (object)Math.Round(rng.NextDouble() * 1000, 4);

            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void InsertReports(SqliteConnection conn, Random rng)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        var colNames = string.Join(", ", Enumerable.Range(1, 20).Select(n => $"col_{n:D2}"));
        var colParams = string.Join(", ", Enumerable.Range(1, 20).Select(n => $"@c{n:D2}"));
        cmd.CommandText = $"INSERT INTO reports (label, {colNames}, notes) VALUES (@lbl, {colParams}, @notes)";

        var pLabel = cmd.Parameters.Add("@lbl", SqliteType.Text);
        var cols = new SqliteParameter[20];
        for (int c = 0; c < 20; c++)
            cols[c] = cmd.Parameters.Add($"@c{c + 1:D2}", SqliteType.Real);
        var pNotes = cmd.Parameters.Add("@notes", SqliteType.Text);

        for (int i = 0; i < 1_000; i++)
        {
            pLabel.Value = $"report_{i:D4}_{new string((char)('A' + rng.Next(26)), rng.Next(5, 25))}";

            for (int c = 0; c < 20; c++)
                cols[c].Value = Math.Round(rng.NextDouble() * 10000, 4);

            pNotes.Value = rng.NextDouble() < 0.6
                ? DBNull.Value
                : new string('N', rng.Next(100, 1000));

            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Creates a small database for quick benchmarks (open/close, metadata).
    /// </summary>
    public static string CreateSmallDatabase(string directory)
    {
        var path = Path.Combine(directory, "bench_small.db");
        if (File.Exists(path)) File.Delete(path);

        using var conn = new SqliteConnection($"Data Source={path};Pooling=false");
        conn.Open();

        Execute(conn, """
            PRAGMA journal_mode=DELETE;
            PRAGMA page_size=4096;
            CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO metadata VALUES ('version', '1.0');
            INSERT INTO metadata VALUES ('created', '2025-01-01');
            CREATE TABLE small_data (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score REAL, data BLOB);
        """);

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO small_data (name, score, data) VALUES (@n, @s, @d)";
        var pName = cmd.Parameters.Add("@n", SqliteType.Text);
        var pScore = cmd.Parameters.Add("@s", SqliteType.Real);
        var pData = cmd.Parameters.Add("@d", SqliteType.Blob);

        for (int i = 0; i < 100; i++)
        {
            pName.Value = $"item_{i:D4}";
            pScore.Value = i * 1.1;
            pData.Value = new byte[16];
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        return path;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
