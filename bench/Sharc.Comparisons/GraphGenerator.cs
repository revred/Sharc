using Microsoft.Data.Sqlite;

namespace Sharc.Comparisons;

public static class GraphGenerator
{
    public static void GenerateSQLite(string dbPath, int nodeCount, int edgeCount)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = DELETE;
            PRAGMA page_size = 4096;
            
            CREATE TABLE _concepts (
                id TEXT NOT NULL,
                key INTEGER PRIMARY KEY,
                type_id INTEGER NOT NULL,
                data TEXT,
                cvn INTEGER,
                lvn INTEGER,
                sync_status INTEGER
            );
            
            CREATE TABLE _relations (
                id TEXT NOT NULL,
                origin INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                target INTEGER NOT NULL,
                data TEXT
            );

            CREATE INDEX idx_concepts_id ON _concepts(id);
            CREATE INDEX idx_relations_target ON _relations(target);
        ";
        cmd.ExecuteNonQuery();

        using var tx = conn.BeginTransaction();
        
        // Insert Nodes
        var nodeCmd = conn.CreateCommand();
        nodeCmd.Transaction = tx;
        nodeCmd.CommandText = "INSERT INTO _concepts (id, key, type_id, data) VALUES ($id, $key, $type, $data)";
        var pId = nodeCmd.Parameters.Add("$id", SqliteType.Text);
        var pKey = nodeCmd.Parameters.Add("$key", SqliteType.Integer);
        var pType = nodeCmd.Parameters.Add("$type", SqliteType.Integer);
        var pData = nodeCmd.Parameters.Add("$data", SqliteType.Text);

        var random = new Random(42);
        
        for (int i = 0; i < nodeCount; i++)
        {
            pId.Value = Guid.NewGuid().ToString();
            pKey.Value = i + 1;
            pType.Value = random.Next(1, 5); // 1=File, 2=Class, 3=Method, 4=Variable
            pData.Value = $"{{\"name\": \"Node_{i}\", \"size\": {random.Next(100, 10000)}}}";
            nodeCmd.ExecuteNonQuery();
        }

        // Insert Edges
        var edgeCmd = conn.CreateCommand();
        edgeCmd.Transaction = tx;
        edgeCmd.CommandText = "INSERT INTO _relations (id, origin, kind, target, data) VALUES ($id, $o, $k, $t, $d)";
        var epId = edgeCmd.Parameters.Add("$id", SqliteType.Text);
        var epOrigin = edgeCmd.Parameters.Add("$o", SqliteType.Integer);
        var epKind = edgeCmd.Parameters.Add("$k", SqliteType.Integer);
        var epTarget = edgeCmd.Parameters.Add("$t", SqliteType.Integer);
        var epData = edgeCmd.Parameters.Add("$d", SqliteType.Text);

        for (int i = 0; i < edgeCount; i++)
        {
            epId.Value = Guid.NewGuid().ToString();
            epOrigin.Value = random.Next(1, nodeCount + 1);
            epKind.Value = random.Next(10, 20); // 10=Defines, 15=Calls
            epTarget.Value = random.Next(1, nodeCount + 1);
            epData.Value = "{}";
            
            try
            {
                edgeCmd.ExecuteNonQuery();
            }
            catch (SqliteException) 
            {
                // Ignore duplicates on PK violation
            }
        }

        tx.Commit();
        
        // Optimize B-Tree layout
        using var vacuumCmd = conn.CreateCommand();
        vacuumCmd.CommandText = "VACUUM;";
        vacuumCmd.ExecuteNonQuery();
    }
}
