using Microsoft.Data.Sqlite;

namespace Sharc.Benchmarks.Helpers;

/// <summary>
/// Generates real SQLite database files for comparative benchmarks.
/// Uses Pooling=false to ensure files are fully released after creation.
/// </summary>
internal static class TestDatabaseGenerator
{
    public static string CreateSmallDatabase(string directory)
    {
        var path = Path.Combine(directory, "bench_small.db");
        if (File.Exists(path)) File.Delete(path);

        using (var conn = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = """
                    PRAGMA journal_mode=DELETE;
                    PRAGMA page_size=4096;
                    CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    INSERT INTO metadata VALUES ('version', '1.0');
                    INSERT INTO metadata VALUES ('created', '2025-01-01');
                    CREATE TABLE small_data (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score REAL, data BLOB);
                """;
                ddl.ExecuteNonQuery();
            }

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
        }
        return path;
    }

    public static string CreateMediumDatabase(string directory)
    {
        var path = Path.Combine(directory, "bench_medium.db");
        if (File.Exists(path)) File.Delete(path);

        using (var conn = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = """
                    PRAGMA journal_mode=DELETE;
                    PRAGMA page_size=4096;
                    CREATE TABLE users (
                        id INTEGER PRIMARY KEY, username TEXT NOT NULL, email TEXT NOT NULL,
                        age INTEGER, balance REAL, avatar BLOB
                    );
                    CREATE INDEX idx_username ON users(username);
                """;
                ddl.ExecuteNonQuery();
            }

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO users (username, email, age, balance, avatar) VALUES (@u, @e, @a, @b, @av)";
            var pUser = cmd.Parameters.Add("@u", SqliteType.Text);
            var pEmail = cmd.Parameters.Add("@e", SqliteType.Text);
            var pAge = cmd.Parameters.Add("@a", SqliteType.Integer);
            var pBalance = cmd.Parameters.Add("@b", SqliteType.Real);
            var pAvatar = cmd.Parameters.Add("@av", SqliteType.Blob);

            var rng = new Random(42);
            for (int i = 0; i < 10_000; i++)
            {
                pUser.Value = $"user_{i:D5}";
                pEmail.Value = $"user_{i:D5}@example.com";
                pAge.Value = 18 + rng.Next(62);
                pBalance.Value = Math.Round(rng.NextDouble() * 10000, 2);
                pAvatar.Value = new byte[64];
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return path;
    }

    public static string CreateLargeDatabase(string directory)
    {
        var path = Path.Combine(directory, "bench_large.db");
        if (File.Exists(path)) File.Delete(path);

        using (var conn = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = """
                    PRAGMA journal_mode=DELETE;
                    PRAGMA page_size=4096;
                    CREATE TABLE events (
                        id INTEGER PRIMARY KEY, timestamp TEXT NOT NULL,
                        level INTEGER NOT NULL, message TEXT NOT NULL, payload BLOB
                    );
                """;
                ddl.ExecuteNonQuery();
            }

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO events (timestamp, level, message, payload) VALUES (@t, @l, @m, @p)";
            var pTime = cmd.Parameters.Add("@t", SqliteType.Text);
            var pLevel = cmd.Parameters.Add("@l", SqliteType.Integer);
            var pMsg = cmd.Parameters.Add("@m", SqliteType.Text);
            var pPayload = cmd.Parameters.Add("@p", SqliteType.Blob);

            var rng = new Random(42);
            var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 100_000; i++)
            {
                pTime.Value = baseTime.AddSeconds(i).ToString("O");
                pLevel.Value = rng.Next(1, 6);
                pMsg.Value = $"Event {i}: Something happened with code {rng.Next(1000, 9999)}";
                pPayload.Value = new byte[rng.Next(0, 256)];
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return path;
    }
}
