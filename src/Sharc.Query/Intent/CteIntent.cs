// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A compiled Common Table Expression: a named sub-query whose results
/// are materialized before the main query executes.
/// </summary>
public sealed class CteIntent
{
    /// <summary>CTE name (used as a virtual table reference).</summary>
    public required string Name { get; init; }

    /// <summary>The sub-query that produces the CTE's rows.</summary>
    public required QueryIntent Query { get; init; }
}
