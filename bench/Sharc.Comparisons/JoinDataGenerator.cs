using Microsoft.Data.Sqlite;

namespace Sharc.Comparisons;

public static class JoinDataGenerator
{
    public static void Generate(string dbPath, int userCount, int ordersPerUser, bool createIndexes = false)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = DELETE;
            PRAGMA page_size = 4096;

            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                dept TEXT NOT NULL
            );

            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER NOT NULL,
                amount REAL NOT NULL,
                status TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        if (createIndexes)
        {
            using var idxCmd = conn.CreateCommand();
            idxCmd.CommandText = @"
                CREATE INDEX idx_orders_user_id ON orders(user_id);
                CREATE INDEX idx_users_dept ON users(dept);
            ";
            idxCmd.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        var rng = new Random(42);

        var userCmd = conn.CreateCommand();
        userCmd.Transaction = tx;
        userCmd.CommandText = "INSERT INTO users (id, name, dept) VALUES ($id, $name, $dept)";
        var pUId = userCmd.Parameters.Add("$id", SqliteType.Integer);
        var pUName = userCmd.Parameters.Add("$name", SqliteType.Text);
        var pUDept = userCmd.Parameters.Add("$dept", SqliteType.Text);

        string[] depts = ["Engineering", "Sales", "Marketing", "HR", "Legal"];

        for (int i = 1; i <= userCount; i++)
        {
            pUId.Value = i;
            pUName.Value = $"User {i}";
            pUDept.Value = depts[rng.Next(depts.Length)];
            userCmd.ExecuteNonQuery();
        }

        var orderCmd = conn.CreateCommand();
        orderCmd.Transaction = tx;
        orderCmd.CommandText = "INSERT INTO orders (id, user_id, amount, status) VALUES ($id, $uid, $amt, $stat)";
        var pOId = orderCmd.Parameters.Add("$id", SqliteType.Integer);
        var pOUid = orderCmd.Parameters.Add("$uid", SqliteType.Integer);
        var pOAmt = orderCmd.Parameters.Add("$amt", SqliteType.Real);
        var pOStat = orderCmd.Parameters.Add("$stat", SqliteType.Text);

        string[] statuses = ["Pending", "Shipped", "Delivered", "Cancelled"];
        int orderId = 1;

        for (int i = 1; i <= userCount; i++)
        {
            for (int j = 0; j < ordersPerUser; j++)
            {
                pOId.Value = orderId++;
                pOUid.Value = i;
                pOAmt.Value = Math.Round(rng.NextDouble() * 1000, 2);
                pOStat.Value = statuses[rng.Next(statuses.Length)];
                orderCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        conn.Close();
        SqliteConnection.ClearAllPools();
    }
}
