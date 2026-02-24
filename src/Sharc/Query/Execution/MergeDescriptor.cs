// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Query.Execution;

/// <summary>
/// Correct-by-construction column ordering for hash join emission paths.
/// Handles three emission scenarios: matched, probe-unmatched, build-unmatched.
/// When <see cref="BuildIsLeft"/> is true, the build side maps to the left
/// portion of the merged row; otherwise it maps to the right.
/// </summary>
internal readonly struct MergeDescriptor
{
    /// <summary>Number of columns from the left (original query) side.</summary>
    public readonly int LeftColumnCount;

    /// <summary>Number of columns from the right (original query) side.</summary>
    public readonly int RightColumnCount;

    /// <summary>Total merged row width (<see cref="LeftColumnCount"/> + <see cref="RightColumnCount"/>).</summary>
    public readonly int MergedWidth;

    /// <summary>
    /// When true, the build side maps to left columns and probe to right.
    /// When false, probe maps to left and build to right.
    /// </summary>
    public readonly bool BuildIsLeft;

    private MergeDescriptor(int leftColumnCount, int rightColumnCount, bool buildIsLeft)
    {
        LeftColumnCount = leftColumnCount;
        RightColumnCount = rightColumnCount;
        MergedWidth = leftColumnCount + rightColumnCount;
        BuildIsLeft = buildIsLeft;
    }

    /// <summary>
    /// Creates a new merge descriptor.
    /// </summary>
    /// <param name="leftColumnCount">Column count for the left table in the original query.</param>
    /// <param name="rightColumnCount">Column count for the right table in the original query.</param>
    /// <param name="buildIsLeft">True if the build side is the left table (e.g., RIGHT JOIN, swapped INNER).</param>
    public static MergeDescriptor Create(int leftColumnCount, int rightColumnCount, bool buildIsLeft)
        => new(leftColumnCount, rightColumnCount, buildIsLeft);

    /// <summary>
    /// Merges a matched probe row and build row into the output buffer.
    /// Output layout is always [left columns, right columns] regardless of which side is build/probe.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MergeMatched(ReadOnlySpan<QueryValue> probeRow, ReadOnlySpan<QueryValue> buildRow, Span<QueryValue> output)
    {
        if (BuildIsLeft)
        {
            // Build = left, Probe = right
            buildRow.CopyTo(output);
            probeRow.CopyTo(output[LeftColumnCount..]);
        }
        else
        {
            // Probe = left, Build = right
            probeRow.CopyTo(output);
            buildRow.CopyTo(output[LeftColumnCount..]);
        }
    }

    /// <summary>
    /// Emits an unmatched probe row with NULLs on the build side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EmitProbeUnmatched(ReadOnlySpan<QueryValue> probeRow, Span<QueryValue> output)
    {
        if (BuildIsLeft)
        {
            // Build = left (nulls), Probe = right
            output[..LeftColumnCount].Fill(QueryValue.Null);
            probeRow.CopyTo(output[LeftColumnCount..]);
        }
        else
        {
            // Probe = left, Build = right (nulls)
            probeRow.CopyTo(output);
            output[LeftColumnCount..].Fill(QueryValue.Null);
        }
    }

    /// <summary>
    /// Emits an unmatched build row with NULLs on the probe side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EmitBuildUnmatched(ReadOnlySpan<QueryValue> buildRow, Span<QueryValue> output)
    {
        if (BuildIsLeft)
        {
            // Build = left, Probe = right (nulls)
            buildRow.CopyTo(output);
            output[LeftColumnCount..].Fill(QueryValue.Null);
        }
        else
        {
            // Build = right, Probe = left (nulls)
            output[..LeftColumnCount].Fill(QueryValue.Null);
            buildRow.CopyTo(output[LeftColumnCount..]);
        }
    }
}
