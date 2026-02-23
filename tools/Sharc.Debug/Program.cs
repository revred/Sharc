// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Graph;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Comparisons;

var dir = Path.Combine(Path.GetTempPath(), "sharc_alloc_debug");
Directory.CreateDirectory(dir);
var dbPath = Path.Combine(dir, "alloc_debug.db");

if (File.Exists(dbPath)) File.Delete(dbPath);
// Generate a database with 5000 nodes and 15000 edges
GraphGenerator.GenerateSQLite(dbPath, nodeCount: 5000, edgeCount: 15000);

using var sharcDb = SharcDatabase.Open(dbPath);
using var graph = new SharcContextGraph(sharcDb.BTreeReader, new NativeSchemaAdapter());
graph.Initialize();

using var sqliteConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
sqliteConn.Open();

const int TargetNode = 500;
const int Iterations = 1000;

Console.WriteLine($"Running {Iterations} iterations for Node {TargetNode}...");

// Warmup
for (int i = 0; i < 10; i++)
{
    graph.Traverse(new NodeKey(TargetNode), new TraversalPolicy { Direction = TraversalDirection.Both, MaxDepth = 1 });
}

// Sharc Allocation Measurement
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long startAllocSharc = GC.GetAllocatedBytesForCurrentThread();
int sharcResultCount = 0;
for (int i = 0; i < Iterations; i++)
{
    var policy = new TraversalPolicy { Direction = TraversalDirection.Both, MaxDepth = 1, IncludePaths = false, IncludeData = false };
    var result = graph.Traverse(new NodeKey(TargetNode), policy);
    sharcResultCount = result.Nodes.Count;
}
long endAllocSharc = GC.GetAllocatedBytesForCurrentThread();
long totalAllocSharc = endAllocSharc - startAllocSharc;

// SQLite Allocation Measurement
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long startAllocSqlite = GC.GetAllocatedBytesForCurrentThread();
int sqliteResultCount = 0;
for (int i = 0; i < Iterations; i++)
{
    sqliteResultCount = RunSQLiteBiDir(sqliteConn, TargetNode);
}
long endAllocSqlite = GC.GetAllocatedBytesForCurrentThread();
long totalAllocSqlite = endAllocSqlite - startAllocSqlite;

Console.WriteLine("\n--- Allocation Results (per iteration) ---");
Console.WriteLine($"Sharc (Traverse 1-Hop): {totalAllocSharc / (double)Iterations:F0} bytes [Count: {sharcResultCount}]");
Console.WriteLine($"SQLite (Bi-Dir Union):  {totalAllocSqlite / (double)Iterations:F0} bytes [Count: {sqliteResultCount}]");
Console.WriteLine($"Ratio (Sharc/SQLite):   {totalAllocSharc / (double)totalAllocSqlite:F2}x");

if (totalAllocSharc <= totalAllocSqlite * 1.5)
{
    Console.WriteLine("\nSUCCESS: Sharc allocations are comparable to SQLite!");
}
else
{
    Console.WriteLine("\nFAIL: Sharc allocations are still too high.");
}

static int RunSQLiteBiDir(SqliteConnection conn, int key)
{
    int count = 0;
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT target_key FROM _relations WHERE source_key = $key UNION SELECT source_key FROM _relations WHERE target_key = $key";
    cmd.Parameters.AddWithValue("$key", key);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        count++;
    }
    return count + 1; // +1 for the start node which Sharc includes
}
