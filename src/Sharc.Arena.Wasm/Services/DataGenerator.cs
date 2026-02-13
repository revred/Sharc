/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Microsoft.Data.Sqlite;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Deterministic data generator that creates an in-memory SQLite database and exports it as
/// a byte array for SharcDatabase.OpenMemory(). Uses a linear congruential PRNG with seed=42
/// so all engines receive identical data.
/// </summary>
public sealed class DataGenerator
{
    private uint _seed;

    private static readonly string[] Departments = ["eng", "sales", "ops", "hr", "marketing"];

    public DataGenerator(uint seed = 42)
    {
        _seed = seed;
    }

    private uint Next()
    {
        _seed = _seed * 1664525 + 1013904223;
        return _seed;
    }

    private int NextInt(int min, int max) => (int)(min + Next() % (uint)(max - min));
    private double NextDouble() => Next() / (double)uint.MaxValue;

    /// <summary>
    /// Creates an in-memory SQLite database with users, events, _concepts, and _relations tables,
    /// populated with deterministic data. Returns the database as a byte array.
    /// </summary>
    /// <param name="userCount">Number of rows in the users table.</param>
    /// <param name="nodeCount">Number of rows in the _concepts table.</param>
    public byte[] GenerateDatabase(int userCount, int nodeCount)
    {
        _seed = 42; // Reset seed for determinism

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        CreateSchema(connection);
        InsertUsers(connection, userCount);
        InsertEvents(connection, userCount);
        InsertConcepts(connection, nodeCount);
        InsertRelations(connection, nodeCount);

        return ExportToBytes(connection);
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                name    TEXT NOT NULL,
                email   TEXT NOT NULL,
                age     INTEGER NOT NULL,
                score   REAL NOT NULL,
                bio     TEXT,
                active  INTEGER NOT NULL,
                dept    TEXT NOT NULL,
                created TEXT NOT NULL
            );

            CREATE TABLE events (
                id        INTEGER PRIMARY KEY,
                user_id   INTEGER NOT NULL,
                type      INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                payload   TEXT NOT NULL
            );

            CREATE TABLE _concepts (
                id    TEXT NOT NULL,
                key   INTEGER UNIQUE NOT NULL,
                kind  INTEGER NOT NULL,
                alias TEXT,
                data  TEXT NOT NULL
            );

            CREATE TABLE _relations (
                id     TEXT NOT NULL,
                origin INTEGER NOT NULL,
                target INTEGER NOT NULL,
                kind   INTEGER NOT NULL,
                weight REAL NOT NULL,
                data   TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void InsertUsers(SqliteConnection connection, int count)
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO users (name, email, age, score, bio, active, dept, created) VALUES ($name, $email, $age, $score, $bio, $active, $dept, $created)";

        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pEmail = cmd.Parameters.Add("$email", SqliteType.Text);
        var pAge = cmd.Parameters.Add("$age", SqliteType.Integer);
        var pScore = cmd.Parameters.Add("$score", SqliteType.Real);
        var pBio = cmd.Parameters.Add("$bio", SqliteType.Text);
        var pActive = cmd.Parameters.Add("$active", SqliteType.Integer);
        var pDept = cmd.Parameters.Add("$dept", SqliteType.Text);
        var pCreated = cmd.Parameters.Add("$created", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < count; i++)
        {
            var nameLen = NextInt(8, 25);
            pName.Value = GenerateName(nameLen);
            pEmail.Value = $"user{i}@domain{i % 10}.com";
            pAge.Value = NextInt(18, 81);
            pScore.Value = Math.Round(NextDouble() * 100, 2);
            pBio.Value = NextDouble() < 0.3 ? DBNull.Value : GenerateText(NextInt(50, 501));
            pActive.Value = NextDouble() < 0.7 ? 1 : 0;
            pDept.Value = Departments[NextInt(0, Departments.Length)];
            pCreated.Value = GenerateDate();
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void InsertEvents(SqliteConnection connection, int userCount)
    {
        var eventCount = userCount * 20;
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO events (id, user_id, type, timestamp, payload) VALUES ($id, $uid, $type, $ts, $payload)";

        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pUid = cmd.Parameters.Add("$uid", SqliteType.Integer);
        var pType = cmd.Parameters.Add("$type", SqliteType.Integer);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
        var pPayload = cmd.Parameters.Add("$payload", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < eventCount; i++)
        {
            pId.Value = i + 1;
            pUid.Value = NextInt(1, userCount + 1);
            pType.Value = NextInt(1, 21);
            pTs.Value = GenerateDate();
            pPayload.Value = $"{{\"action\":\"{GenerateName(8)}\",\"value\":{NextInt(1, 1000)}}}";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void InsertConcepts(SqliteConnection connection, int count)
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO _concepts (id, key, kind, alias, data) VALUES ($id, $key, $kind, $alias, $data)";

        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pKey = cmd.Parameters.Add("$key", SqliteType.Integer);
        var pKind = cmd.Parameters.Add("$kind", SqliteType.Integer);
        var pAlias = cmd.Parameters.Add("$alias", SqliteType.Text);
        var pData = cmd.Parameters.Add("$data", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < count; i++)
        {
            pId.Value = GenerateGuid(i);
            pKey.Value = i + 1;
            pKind.Value = NextInt(1, 101);
            pAlias.Value = $"src/{GenerateName(6)}.cs";
            pData.Value = $"{{\"name\":\"{GenerateName(10)}\",\"size\":{NextInt(100, 5000)}}}";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void InsertRelations(SqliteConnection connection, int nodeCount)
    {
        var edgeCount = nodeCount * 3;
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO _relations (id, origin, target, kind, weight, data) VALUES ($id, $origin, $target, $kind, $weight, $data)";

        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pOrigin = cmd.Parameters.Add("$origin", SqliteType.Integer);
        var pTarget = cmd.Parameters.Add("$target", SqliteType.Integer);
        var pKind = cmd.Parameters.Add("$kind", SqliteType.Integer);
        var pWeight = cmd.Parameters.Add("$weight", SqliteType.Real);
        var pData = cmd.Parameters.Add("$data", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < edgeCount; i++)
        {
            pId.Value = GenerateGuid(1_000_000 + i);
            pOrigin.Value = NextInt(1, nodeCount + 1);
            pTarget.Value = NextInt(1, nodeCount + 1);
            pKind.Value = NextInt(1, 16);
            pWeight.Value = Math.Round(NextDouble(), 3);
            pData.Value = $"{{\"ref\":\"{GenerateName(8)}\"}}";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static byte[] ExportToBytes(SqliteConnection connection)
    {
        // Use SQLite backup API to export in-memory DB to a byte array
        using var destConnection = new SqliteConnection("Data Source=:memory:");
        destConnection.Open();

        using var srcRaw = connection;
        connection.BackupDatabase(destConnection);

        // Now read from dest using VACUUM INTO a temp file approach
        // Actually, simplest: serialize via the backup API to another in-memory DB,
        // then export via custom serialization.
        // Cleanest approach: write to temp file and read back
        var tempPath = Path.GetTempFileName();
        try
        {
            using (var fileConnection = new SqliteConnection($"Data Source={tempPath}"))
            {
                fileConnection.Open();
                connection.BackupDatabase(fileConnection);
            }
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private string GenerateName(int length)
    {
        Span<char> buffer = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            if (i > 0 && NextDouble() < 0.15)
                buffer[i] = ' ';
            else
                buffer[i] = (char)('a' + NextInt(0, 26));
        }
        return new string(buffer);
    }

    private string GenerateText(int length)
    {
        Span<char> buffer = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            if (NextDouble() < 0.12)
                buffer[i] = ' ';
            else
                buffer[i] = (char)('a' + NextInt(0, 26));
        }
        return new string(buffer);
    }

    private string GenerateDate()
    {
        var year = NextInt(2020, 2026);
        var month = NextInt(1, 13);
        var day = NextInt(1, 29);
        var hour = NextInt(0, 24);
        var min = NextInt(0, 60);
        var sec = NextInt(0, 60);
        return $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{min:D2}:{sec:D2}Z";
    }

    private static string GenerateGuid(int seed)
    {
        // Deterministic GUID-like string from seed
        var hash = (uint)seed * 2654435761u;
        return $"{hash:x8}-{(hash >> 4):x4}-4{(hash >> 8) & 0xFFF:x3}-{0x8 | ((hash >> 12) & 0x3):x1}{(hash >> 16) & 0xFFF:x3}-{hash ^ 0xDEADBEEF:x12}";
    }
}
