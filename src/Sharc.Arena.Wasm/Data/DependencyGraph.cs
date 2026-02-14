// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Data;

/// <summary>
/// Defines the prerequisite chain between slides.
/// A slide can only run after its prerequisites are complete.
/// </summary>
public static class DependencyGraph
{
    /// <summary>
    /// Returns prerequisites for a given slide ID.
    /// Slides with no prerequisites return an empty array.
    /// </summary>
    public static IReadOnlyList<string> GetPrerequisites(string slideId) => slideId switch
    {
        "schema-read"     => ["engine-load"],
        "sequential-scan" => ["engine-load"],
        "point-lookup"    => ["engine-load"],
        "batch-lookup"    => ["point-lookup"],
        "type-decode"     => ["sequential-scan"],
        "null-scan"       => ["sequential-scan"],
        "where-filter"    => ["sequential-scan"],
        "graph-node-scan" => ["engine-load"],
        "graph-edge-scan" => ["graph-node-scan"],
        "graph-seek"      => ["graph-node-scan"],
        "graph-traverse"  => ["graph-seek", "graph-edge-scan"],
        "gc-pressure"     => ["sequential-scan"],
        "encryption"      => ["engine-load"],
        _                 => [],
    };
}