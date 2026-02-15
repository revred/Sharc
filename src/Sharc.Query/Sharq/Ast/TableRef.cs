// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>
/// A table reference in a FROM clause, with optional alias and record ID.
/// </summary>
internal sealed class TableRef
{
    public required string Name { get; init; }
    public string? Alias { get; init; }
    /// <summary>Record ID portion if present (e.g., "alice" in person:alice).</summary>
    public string? RecordId { get; init; }
}
