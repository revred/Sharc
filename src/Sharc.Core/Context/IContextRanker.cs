#pragma warning disable CS1591

// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Context;

/// <summary>
/// Ranks context nodes for a task under a token budget.
/// </summary>
public interface IContextRanker
{
    IEnumerable<ContextNode> Rank(string query, ContextScope scope);
}

#pragma warning restore CS1591

