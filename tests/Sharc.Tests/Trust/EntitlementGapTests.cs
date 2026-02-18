using Sharc.Core.Trust;
using Sharc.Trust;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Tests.Trust;

public class EntitlementGapTests
{
    private static AgentInfo MakeAgent(string readScope) =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "*", readScope, 0, 0, "", false, Array.Empty<byte>());

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

    /*
    [Fact]
    public void View_Gap_Accesses_Denied_Table_If_Enforced_After_Resolution()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"entitlement_gap_view_{Guid.NewGuid()}.db");
        SetupGapViewDb(dbPath);

        try
        {
            using (var db = SharcDatabase.Open(dbPath))
            {
                var agent = MakeAgent("v_public.*"); 
                // Currently passes. If strictly enforced on base, might throw.
                // Keeping commented out until behavior is finalized.
            }
        }
        finally
        {
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }
    */

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
