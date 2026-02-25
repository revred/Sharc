// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>A single similarity match with distance score and optional metadata.</summary>
public readonly record struct VectorMatch(
    long RowId,
    float Distance,
    IReadOnlyDictionary<string, object?>? Metadata = null);
