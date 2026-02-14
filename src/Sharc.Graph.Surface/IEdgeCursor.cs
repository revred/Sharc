// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// A forward-only, zero-allocation cursor over graph edges originating from a single node.
/// Avoids <see cref="GraphEdge"/> allocation per row â€” callers read typed properties directly.
/// The cursor is valid until disposed. Property values are valid until the next <see cref="MoveNext"/> call.
/// </summary>
public interface IEdgeCursor : IDisposable
{
    /// <summary>Advances to the next matching edge. Returns false when exhausted.</summary>
    bool MoveNext();

    /// <summary>Resets the cursor to a new origin and kind without allocation.</summary>
    void Reset(long matchKey, int? matchKind = null);

    /// <summary>The integer key of the origin node.</summary>
    long OriginKey { get; }

    /// <summary>The integer key of the target node.</summary>
    long TargetKey { get; }

    /// <summary>The edge kind/link ID.</summary>
    int Kind { get; }

    /// <summary>Edge relevance weight (0.0 to 1.0).</summary>
    float Weight { get; }

    /// <summary>
    /// The raw UTF-8 bytes of the edge JSON data. Zero-allocation â€” avoids string materialization.
    /// Returns empty if the data column is not present or is NULL.
    /// </summary>
    ReadOnlyMemory<byte> JsonDataUtf8 { get; }
}