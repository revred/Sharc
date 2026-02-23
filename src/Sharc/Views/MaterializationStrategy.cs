// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Views;

/// <summary>
/// Controls how an <see cref="ILayer"/> materializes rows during cursor iteration.
/// </summary>
public enum MaterializationStrategy : byte
{
    /// <summary>Materialize all rows upfront. Stable for ORDER BY, aggregations, repeated access.</summary>
    Eager = 0,

    /// <summary>Stream rows on demand via cursor. Lower memory, forward-only. Ideal for LIMIT and ETL.</summary>
    Streaming = 1,
}
