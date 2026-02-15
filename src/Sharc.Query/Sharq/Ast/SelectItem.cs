// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq.Ast;

/// <summary>
/// A single item in a SELECT list: an expression with an optional alias.
/// </summary>
internal sealed class SelectItem
{
    public required SharqStar Expression { get; init; }
    public string? Alias { get; init; }
}
