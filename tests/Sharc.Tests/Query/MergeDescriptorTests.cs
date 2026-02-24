// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Tests for <see cref="MergeDescriptor"/> — correct-by-construction column ordering
/// across three emission paths: matched, probe-unmatched, build-unmatched.
/// </summary>
public sealed class MergeDescriptorTests
{
    [Fact]
    public void Create_NotSwapped_SetsFieldsCorrectly()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 3, rightColumnCount: 2, buildIsLeft: false);

        Assert.Equal(3, desc.LeftColumnCount);
        Assert.Equal(2, desc.RightColumnCount);
        Assert.Equal(5, desc.MergedWidth);
        Assert.False(desc.BuildIsLeft);
    }

    [Fact]
    public void Create_Swapped_SetsFieldsCorrectly()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 4, rightColumnCount: 3, buildIsLeft: true);

        Assert.Equal(4, desc.LeftColumnCount);
        Assert.Equal(3, desc.RightColumnCount);
        Assert.Equal(7, desc.MergedWidth);
        Assert.True(desc.BuildIsLeft);
    }

    [Fact]
    public void MergeMatched_NotSwapped_ProbeLeftBuildRight()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: false);

        var probe = new QueryValue[] { QueryValue.FromInt64(1), QueryValue.FromString("a") };
        var build = new QueryValue[] { QueryValue.FromInt64(2), QueryValue.FromString("b") };
        var output = new QueryValue[4];

        desc.MergeMatched(probe, build, output);

        Assert.Equal(1L, output[0].AsInt64());
        Assert.Equal("a", output[1].AsString());
        Assert.Equal(2L, output[2].AsInt64());
        Assert.Equal("b", output[3].AsString());
    }

    [Fact]
    public void MergeMatched_Swapped_BuildLeftProbeRight()
    {
        // When buildIsLeft=true, build side is left in the original query.
        // The merged output should still be [left, right] = [build, probe].
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: true);

        var probe = new QueryValue[] { QueryValue.FromInt64(10), QueryValue.FromString("x") };
        var build = new QueryValue[] { QueryValue.FromInt64(20), QueryValue.FromString("y") };
        var output = new QueryValue[4];

        desc.MergeMatched(probe, build, output);

        // Build is left, so output = [build, probe]
        Assert.Equal(20L, output[0].AsInt64());
        Assert.Equal("y", output[1].AsString());
        Assert.Equal(10L, output[2].AsInt64());
        Assert.Equal("x", output[3].AsString());
    }

    [Fact]
    public void EmitProbeUnmatched_NotSwapped_ProbeLeftNullRight()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: false);

        var probe = new QueryValue[] { QueryValue.FromInt64(5), QueryValue.FromString("c") };
        var output = new QueryValue[4];

        desc.EmitProbeUnmatched(probe, output);

        Assert.Equal(5L, output[0].AsInt64());
        Assert.Equal("c", output[1].AsString());
        Assert.True(output[2].IsNull);
        Assert.True(output[3].IsNull);
    }

    [Fact]
    public void EmitProbeUnmatched_Swapped_NullLeftProbeRight()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: true);

        var probe = new QueryValue[] { QueryValue.FromInt64(5), QueryValue.FromString("c") };
        var output = new QueryValue[4];

        desc.EmitProbeUnmatched(probe, output);

        // Probe is right side when buildIsLeft=true, so output = [null, probe]
        Assert.True(output[0].IsNull);
        Assert.True(output[1].IsNull);
        Assert.Equal(5L, output[2].AsInt64());
        Assert.Equal("c", output[3].AsString());
    }

    [Fact]
    public void EmitBuildUnmatched_NotSwapped_NullLeftBuildRight()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: false);

        var build = new QueryValue[] { QueryValue.FromInt64(9), QueryValue.FromString("d") };
        var output = new QueryValue[4];

        desc.EmitBuildUnmatched(build, output);

        // Build is right side, so output = [null, build]
        Assert.True(output[0].IsNull);
        Assert.True(output[1].IsNull);
        Assert.Equal(9L, output[2].AsInt64());
        Assert.Equal("d", output[3].AsString());
    }

    [Fact]
    public void EmitBuildUnmatched_Swapped_BuildLeftNullRight()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 2, rightColumnCount: 2, buildIsLeft: true);

        var build = new QueryValue[] { QueryValue.FromInt64(9), QueryValue.FromString("d") };
        var output = new QueryValue[4];

        desc.EmitBuildUnmatched(build, output);

        // Build is left side, so output = [build, null]
        Assert.Equal(9L, output[0].AsInt64());
        Assert.Equal("d", output[1].AsString());
        Assert.True(output[2].IsNull);
        Assert.True(output[3].IsNull);
    }

    [Fact]
    public void MergedWidth_EqualsSum()
    {
        var desc = MergeDescriptor.Create(5, 3, false);
        Assert.Equal(8, desc.MergedWidth);
    }

    [Fact]
    public void AsymmetricColumns_MergeMatched_CorrectLayout()
    {
        var desc = MergeDescriptor.Create(
            leftColumnCount: 3, rightColumnCount: 1, buildIsLeft: false);

        var probe = new QueryValue[] { QueryValue.FromInt64(1), QueryValue.FromInt64(2), QueryValue.FromInt64(3) };
        var build = new QueryValue[] { QueryValue.FromString("z") };
        var output = new QueryValue[4];

        desc.MergeMatched(probe, build, output);

        Assert.Equal(1L, output[0].AsInt64());
        Assert.Equal(2L, output[1].AsInt64());
        Assert.Equal(3L, output[2].AsInt64());
        Assert.Equal("z", output[3].AsString());
    }
}
