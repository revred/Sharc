using Sharc.Core.Trust;
using Sharc.Trust;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Tests.Trust;

public class EntitlementGapTests
{
    private static AgentInfo MakeAgent(string readScope, string writeScope = "*") =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            writeScope, readScope, 0, 0, "", false, Array.Empty<byte>());

    private void SetupGapJoinDb(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_join_{Guid.NewGuid()}.db");
        SetupGapJoinDb(dbPath);

        try
        {
            using (var db = SharcDatabase.Open(dbPath))
            {
                var agent = MakeAgent("public_orders.*");
                
                // This SHOULD throw UnauthorizedAccessException for 'secret_users'
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    db.Query(
                        new Dictionary<string, object>(), 
                        "SELECT * FROM public_orders JOIN secret_users ON public_orders.user_id = secret_users.id", 
                        agent
                    );
                });
            }
        }
        finally
        {
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void Denied_Column_In_Where_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_column_where_{Guid.NewGuid()}.db");
        SetupGapJoinDb(dbPath); // secret_users(id, name, salary)

        try
        {
            using (var db = SharcDatabase.Open(dbPath))
            {
                // Agent can read secret_users.id and secret_users.name, but NOT salary
                var agent = MakeAgent("secret_users.id, secret_users.name");
                
                // Querying name but filtering on salary SHOULD throw
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    db.Query(new Dictionary<string, object>(), "SELECT name FROM secret_users WHERE salary > 50000", agent);
                });
            }
        }
        finally { if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { } }
    }

    [Fact]
    public void Denied_Column_In_Order_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_column_order_{Guid.NewGuid()}.db");
        SetupGapJoinDb(dbPath);

        try
        {
            using (var db = SharcDatabase.Open(dbPath))
            {
                var agent = MakeAgent("secret_users.id, secret_users.name");
                
                // Sorting by salary should throw
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    db.Query(new Dictionary<string, object>(), "SELECT name FROM secret_users ORDER BY salary DESC", agent);
                });
            }
        }
        finally { if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { } }
    }

    [Fact]
    public void Denied_Column_In_Write_Payload_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_write_col_{Guid.NewGuid()}.db");
        SetupGapJoinDb(dbPath);

        try
        {
            using (var writer = SharcWriter.Open(dbPath))
            {
                // Agent can write 'id' and 'name', but NOT 'salary'
                var agent = MakeAgent("*", "secret_users.id, secret_users.name");
                
                // Updating id/name/salary should throw because salary is restricted
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    writer.Update(agent, "secret_users", 100, (long)100, "Alice Updated", 120000.0);
                });
            }
        }
        finally { if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { } }
    }

    [Fact]
    public void Transaction_Entitlement_Enforced()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_tx_{Guid.NewGuid()}.db");
        SetupGapJoinDb(dbPath);

        try
        {
            using (var writer = SharcWriter.Open(dbPath))
            {
                var agent = MakeAgent("*", "public_orders.*"); // Denies secret_users
                
                using var tx = writer.BeginTransaction(agent);
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    tx.Insert("secret_users", (long)101, "Eve", 50000.0);
                });
            }
        }
        finally { if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { } }
    }

    [Fact]
    public void Validity_Unit_Consistency_Is_MS()
    {
        var agent = new AgentInfo("v-agent", AgentClass.User, Array.Empty<byte>(), 0, "*", "*",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), 0, "", false, Array.Empty<byte>());
        
        // Should throw because start time is in future (using MS)
        Assert.Throws<UnauthorizedAccessException>(() => EntitlementEnforcer.Enforce(agent, "any", null));

        var activeAgent = new AgentInfo("a-agent", AgentClass.User, Array.Empty<byte>(), 0, "*", "*",
            DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(), 0, "", false, Array.Empty<byte>());
        
        // Should not throw (in the past)
        EntitlementEnforcer.Enforce(activeAgent, "any", null);
    }

    private void SetupGapViewDb(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE secret_data (id INTEGER, payload TEXT);
            INSERT INTO secret_data VALUES (1, 'Top Secret');
            CREATE VIEW v_public AS SELECT * FROM secret_data;
        ";
        cmd.ExecuteNonQuery();
    }

    private void SetupGapViewJoinDb(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE t1 (id INTEGER);
            CREATE TABLE t2_secret (id INTEGER);
            CREATE VIEW v_join AS SELECT * FROM t1 JOIN t2_secret ON t1.id = t2_secret.id;
        ";
        cmd.ExecuteNonQuery();
    }
    
    [Fact]
    public void View_With_Join_Gap_Accesses_Denied_Table()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_view_join_{Guid.NewGuid()}.db");
        SetupGapViewJoinDb(dbPath);

        try
        {
            using (var db = SharcDatabase.Open(dbPath))
            {
                var agent = MakeAgent("v_join.*");

                // Assuming strict enforcement on underlying tables:
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    db.Query(new Dictionary<string, object>(), "SELECT * FROM v_join", agent);
                });
            }
        }
        finally
        {
             if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }
}
