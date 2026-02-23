// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Tests.Trust;

public class EntitlementGapTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private static AgentInfo MakeAgent(string readScope, string writeScope = "*") =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            writeScope, readScope, 0, 0, "", false, Array.Empty<byte>());

    private string CreateTempDb(Action<SqliteConnection> setup)
    {
        var path = Path.Combine(Path.GetTempPath(), $"entitlement_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        setup(connection);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    private static void SetupGapJoinSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE public_orders (id INTEGER, user_id INTEGER);
            CREATE TABLE secret_users (id INTEGER, name TEXT, salary REAL);
            INSERT INTO public_orders VALUES (1, 100);
            INSERT INTO secret_users VALUES (100, 'Alice', 100000.0);
        ";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Join_Gap_Accesses_Denied_Table()
    {
        var dbPath = CreateTempDb(SetupGapJoinSchema);

        using var db = SharcDatabase.Open(dbPath);
        var agent = MakeAgent("public_orders.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            db.Query(
                new Dictionary<string, object>(),
                "SELECT * FROM public_orders JOIN secret_users ON public_orders.user_id = secret_users.id",
                agent
            );
        });
    }

    [Fact]
    public void Denied_Column_In_Where_Throws()
    {
        var dbPath = CreateTempDb(SetupGapJoinSchema);

        using var db = SharcDatabase.Open(dbPath);
        var agent = MakeAgent("secret_users.id, secret_users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            db.Query(new Dictionary<string, object>(), "SELECT name FROM secret_users WHERE salary > 50000", agent);
        });
    }

    [Fact]
    public void Denied_Column_In_Order_Throws()
    {
        var dbPath = CreateTempDb(SetupGapJoinSchema);

        using var db = SharcDatabase.Open(dbPath);
        var agent = MakeAgent("secret_users.id, secret_users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            db.Query(new Dictionary<string, object>(), "SELECT name FROM secret_users ORDER BY salary DESC", agent);
        });
    }

    [Fact]
    public void Denied_Column_In_Write_Payload_Throws()
    {
        var dbPath = CreateTempDb(SetupGapJoinSchema);

        using var writer = SharcWriter.Open(dbPath);
        var agent = MakeAgent("*", "secret_users.id, secret_users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            writer.Update(agent, "secret_users", 100, (long)100, "Alice Updated", 120000.0);
        });
    }

    [Fact]
    public void Transaction_Entitlement_Enforced()
    {
        var dbPath = CreateTempDb(SetupGapJoinSchema);

        using var writer = SharcWriter.Open(dbPath);
        var agent = MakeAgent("*", "public_orders.*");

        using var tx = writer.BeginTransaction(agent);
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            tx.Insert("secret_users", (long)101, "Eve", 50000.0);
        });
    }

    [Fact]
    public void Validity_Unit_Consistency_Is_MS()
    {
        var agent = new AgentInfo("v-agent", AgentClass.User, Array.Empty<byte>(), 0, "*", "*",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), 0, "", false, Array.Empty<byte>());

        Assert.Throws<UnauthorizedAccessException>(() => EntitlementEnforcer.Enforce(agent, "any", null));

        var activeAgent = new AgentInfo("a-agent", AgentClass.User, Array.Empty<byte>(), 0, "*", "*",
            DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(), 0, "", false, Array.Empty<byte>());

        EntitlementEnforcer.Enforce(activeAgent, "any", null);
    }

    [Fact]
    public void View_With_Join_Gap_Accesses_Denied_Table()
    {
        var dbPath = CreateTempDb(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE t1 (id INTEGER);
                CREATE TABLE t2_secret (id INTEGER);
                CREATE VIEW v_join AS SELECT * FROM t1 JOIN t2_secret ON t1.id = t2_secret.id;
            ";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.Open(dbPath);
        var agent = MakeAgent("v_join.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            db.Query(new Dictionary<string, object>(), "SELECT * FROM v_join", agent);
        });
    }
}
