// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// Result of a <see cref="PreparedAgent"/> execution, reporting
/// the number of steps completed, inserted row IDs, and rows affected
/// by delete/update operations.
/// </summary>
public sealed class PreparedAgentResult
{
    /// <summary>Total number of steps executed.</summary>
    public required int StepsExecuted { get; init; }

    /// <summary>Row IDs assigned by insert steps, in execution order.</summary>
    public required long[] InsertedRowIds { get; init; }

    /// <summary>Total rows affected by delete and update steps.</summary>
    public required int RowsAffected { get; init; }
}
