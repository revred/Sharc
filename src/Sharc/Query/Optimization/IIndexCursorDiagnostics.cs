// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Optimization;

/// <summary>
/// Exposes index-cursor scan diagnostics for query execution reporting.
/// </summary>
internal interface IIndexCursorDiagnostics
{
    /// <summary>Number of index entries examined.</summary>
    int IndexEntriesScanned { get; }

    /// <summary>Number of index entries that matched planned predicates.</summary>
    int IndexHits { get; }
}
