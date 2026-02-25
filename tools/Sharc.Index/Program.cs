// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc;
using Sharc.Index;

// Parse CLI arguments
string repoPath = ".";
string outputPath = "context.db";
string? since = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--repo" when i + 1 < args.Length:
            repoPath = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--since" when i + 1 < args.Length:
            since = args[++i];
            break;
        case "--help":
            Console.WriteLine("sharc-index: Build a GitHub Context Database (GCD) from git history");
            Console.WriteLine();
            Console.WriteLine("Usage: sharc-index [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --repo <path>     Repository path (default: current directory)");
            Console.WriteLine("  --output <path>   Output .db file (default: context.db)");
            Console.WriteLine("  --since <date>    Only include commits after this date");
            Console.WriteLine("  --help            Show this help message");
            return;
    }
}

Console.WriteLine("sharc-index: Building context database");
Console.WriteLine($"  Repo:   {Path.GetFullPath(repoPath)}");
Console.WriteLine($"  Output: {Path.GetFullPath(outputPath)}");
if (since != null)
    Console.WriteLine($"  Since:  {since}");

// Step 1: Create schema
Console.Write("Creating schema... ");
GcdSchemaBuilder.CreateSchema(outputPath);
Console.WriteLine("done.");

// Step 2: Walk git log
Console.Write("Reading git log... ");
var walker = new GitLogWalker(repoPath);
var commits = await walker.GetCommitsAsync(since);
Console.WriteLine($"{commits.Count} commits found.");

// Step 3: Write commits and file changes
using var db = SharcDatabase.Open(outputPath);
using var writer = new CommitWriter(db);

Console.Write("Writing commits... ");
writer.WriteCommits(commits);
Console.WriteLine("done.");

int totalChanges = 0;
Console.Write("Processing file changes... ");
foreach (var commit in commits)
{
    var changes = await walker.GetFileChangesAsync(commit.Sha);
    writer.WriteFileChanges(changes);
    totalChanges += changes.Count;
}
Console.WriteLine($"{totalChanges} file changes written.");

Console.WriteLine($"Context database built: {Path.GetFullPath(outputPath)}");
