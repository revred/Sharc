// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A compiled Cote (Common Table Expression): a named sub-query whose results
/// are materialized before the main query executes.
/// </summary>
public sealed class CoteIntent
{
    /// <summary>Cote name (used as a virtual table reference).</summary>
    public required string Name { get; init; }

    /// <summary>The sub-query (simple or compound) that produces the Cote's rows.</summary>
    public required QueryPlan Query { get; init; }
}
