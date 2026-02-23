// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

﻿using System.Diagnostics;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project tests/Sharc.TestRunner [1|2|All]");
    Console.WriteLine("  1: Storage & Core Engine (~470 tests)");
    Console.WriteLine("  2: Features & Integration (~460 tests)");
    Console.WriteLine("  All: Run all tests");
    return;
}

string group = args[0];
string[] group1Filters = 
[
    "FullyQualifiedName~Sharc.Tests.BTree",
    "FullyQualifiedName~Sharc.Tests.IO",
    "FullyQualifiedName~Sharc.Tests.Records",
    "FullyQualifiedName~Sharc.Tests.Format",
    "FullyQualifiedName~Sharc.Tests.DataStructures",
    "FullyQualifiedName~Sharc.Tests.Crypto",
    "FullyQualifiedName~Sharc.Tests.Schema"
];

string[] group2Filters = 
[
    "FullyQualifiedName~Sharc.Tests.Write",
    "FullyQualifiedName~Sharc.Tests.Filter",
    "FullyQualifiedName~Sharc.Tests.Query",
    "FullyQualifiedName~Sharc.Tests.Trust",
    "FullyQualifiedName~Sharc.Tests.SharcDatabaseApiTests",
    "FullyQualifiedName~Sharc.Tests.TransactionTests",
    "FullyQualifiedName~Sharc.Tests.LedgerTests"
];

string? filter = null;

if (group == "1")
{
    filter = string.Join("|", group1Filters);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Running Group 1 (Storage/Core)...");
    Console.ResetColor();
}
else if (group == "2")
{
    filter = string.Join("|", group2Filters);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Running Group 2 (Features/API)...");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("Running All Tests...");
    Console.ResetColor();
}

RunDotNetTest(filter);

void RunDotNetTest(string? filterExpression)
{
    // Find Sharc.sln by walking up from BaseDirectory
    string baseDir = AppContext.BaseDirectory;
    string slnPath = "";
    
    // Attempt 1: relative to bin/Debug/net10.0/ (4 levels up)
    string candidate1 = Path.GetFullPath(Path.Combine(baseDir, "../../../../Sharc.sln"));
    // Attempt 2: assuming CWD is repo root
    string candidate2 = Path.GetFullPath("Sharc.sln");
    // Attempt 3: one level up
    string candidate3 = Path.GetFullPath("../Sharc.sln");

    if (File.Exists(candidate1)) slnPath = candidate1;
    else if (File.Exists(candidate2)) slnPath = candidate2;
    else if (File.Exists(candidate3)) slnPath = candidate3;
    else 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: Could not locate Sharc.sln.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine($"Found Solution: {slnPath}");

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"test \"{slnPath}\" --configuration Release --no-build" + (string.IsNullOrEmpty(filterExpression) ? "" : $" --filter \"{filterExpression}\""),
        UseShellExecute = false
    };

    Console.WriteLine($"Executing: dotnet {psi.Arguments}");
    
    using var proc = Process.Start(psi);
    proc?.WaitForExit();
}
