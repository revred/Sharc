using Sharc.Graph;
using Sharc;

Console.WriteLine("Sharc Context Graph Sample");
Console.WriteLine("--------------------------");

using var db = SharcDatabase.Open("context.db");
var graph = SharcContextGraph.Create(db);

// Verify the data trust chain before reading
if (graph.Ledger.VerifyIntegrity())
{
    Console.WriteLine("TRUST VERIFIED: Hash chain is intact and signatures are valid.");
}
else
{
    Console.WriteLine("WARNING: Trust chain compromised!");
}

// Traverse relationship from a specific node
string startNode = "Project-Alpha";
Console.WriteLine($"\nNeighbors for '{startNode}':");

var edges = graph.GetEdges(startNode, TraversalDirection.Outgoing);
while (edges.MoveNext())
{
    Console.WriteLine($" -> [{edges.Kind}] {edges.TargetKey}");
}
