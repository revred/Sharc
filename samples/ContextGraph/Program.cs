using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Graph;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;

// --- Generate a graph database ---
var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_graph.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE _concepts (id TEXT PRIMARY KEY, key INTEGER UNIQUE, kind INTEGER, data TEXT,
            cvn INTEGER DEFAULT 0, lvn INTEGER DEFAULT 0, sync_status INTEGER DEFAULT 0,
            updated_at TEXT, alias TEXT, tokens INTEGER DEFAULT 0);
        CREATE TABLE _relations (id TEXT PRIMARY KEY, source_key INTEGER, target_key INTEGER,
            kind INTEGER, data TEXT, cvn INTEGER DEFAULT 0, lvn INTEGER DEFAULT 0,
            sync_status INTEGER DEFAULT 0, weight REAL DEFAULT 1.0);
        CREATE INDEX idx_concepts_key ON _concepts(key);
        CREATE INDEX idx_relations_source_kind ON _relations(source_key, kind, target_key);
        CREATE INDEX idx_relations_target_kind ON _relations(target_key, kind, source_key);

        INSERT INTO _concepts VALUES ('c1', 1, 1, '{"name":"Alice"}', 0,0,0,NULL,NULL,0);
        INSERT INTO _concepts VALUES ('c2', 2, 1, '{"name":"Bob"}', 0,0,0,NULL,NULL,0);
        INSERT INTO _concepts VALUES ('c3', 3, 1, '{"name":"Charlie"}', 0,0,0,NULL,NULL,0);
        INSERT INTO _concepts VALUES ('c4', 4, 2, '{"name":"Project-Alpha"}', 0,0,0,NULL,NULL,0);
        INSERT INTO _concepts VALUES ('c5', 5, 2, '{"name":"Project-Beta"}', 0,0,0,NULL,NULL,0);

        INSERT INTO _relations VALUES ('r1', 1, 4, 1, NULL, 0,0,0,1.0);
        INSERT INTO _relations VALUES ('r2', 2, 4, 1, NULL, 0,0,0,1.0);
        INSERT INTO _relations VALUES ('r3', 2, 5, 1, NULL, 0,0,0,1.0);
        INSERT INTO _relations VALUES ('r4', 3, 5, 1, NULL, 0,0,0,1.0);
        INSERT INTO _relations VALUES ('r5', 1, 2, 2, NULL, 0,0,0,0.8);
        """;
    cmd.ExecuteNonQuery();
}

// --- Traverse with Sharc Graph ---
Console.WriteLine("Sharc Context Graph Sample");
Console.WriteLine("--------------------------");

using var db = SharcDatabase.Open(dbPath);
using var graph = new SharcContextGraph(db.BTreeReader, new NativeSchemaAdapter());
graph.Initialize();

// Look up a specific node
var node = graph.GetNode(new NodeKey(1));
Console.WriteLine($"Node 1: {node?.JsonData}");

// Get outgoing edges
Console.WriteLine("\nOutgoing edges from Node 1:");
foreach (var edge in graph.GetEdges(new NodeKey(1)))
    Console.WriteLine($"  -> target={edge.TargetKey}, kind={edge.Kind}");

// 2-hop BFS traversal
Console.WriteLine("\n2-Hop BFS from Node 1:");
var policy = new TraversalPolicy { Direction = TraversalDirection.Both, MaxDepth = 2 };
var result = graph.Traverse(new NodeKey(1), policy);
Console.WriteLine($"  Reached {result.Nodes.Count} nodes.");
foreach (var n in result.Nodes)
    Console.WriteLine($"  - Key={n.Record.Key}, Data={n.Record.JsonData}");
