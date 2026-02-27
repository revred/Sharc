// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Context;

namespace Sharc.Context;

/// <summary>
/// Naive single-hop dependency analyzer for the open-source Sharc package.
/// Performs a direct JOIN on the file_deps table to find immediate dependents.
/// For production use, replace with a multi-hop heuristic analyzer via DI.
/// </summary>
public sealed class DirectDependencyAnalyzer : IImpactAnalyzer
{
    private readonly SharcDatabase _db;

    /// <summary>
    /// Initializes a new direct dependency analyzer backed by the specified database.
    /// </summary>
    public DirectDependencyAnalyzer(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public ImpactReport CalculateImpact(string targetPath, ImpactOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targetPath);

        var directDeps = new List<ImpactedFile>();

        // Query 1-hop: files that depend on targetPath
        if (_db.Schema.TryGetTable("file_deps") is not null)
        {
            using var reader = _db.CreateReader("file_deps", "source_path", "target_path");
            while (reader.Read())
            {
                var target = reader.GetString(1);
                if (string.Equals(target, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    directDeps.Add(new ImpactedFile
                    {
                        Path = reader.GetString(0) ?? "",
                        HopDistance = 1,
                        TestCoverage = 0.0 // naive: no coverage data
                    });
                }
            }
        }

        return new ImpactReport
        {
            DirectDependencies = directDeps,
            TransitiveDependencies = [], // naive: no multi-hop
            RiskScore = directDeps.Count > 10 ? 0.8 : directDeps.Count > 3 ? 0.4 : 0.1,
            SuggestedTestDepth = directDeps.Count > 10 ? "e2e" : directDeps.Count > 3 ? "integration" : "unit"
        };
    }
}
