// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswConfigTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var config = HnswConfig.Default;

        Assert.Equal(16, config.M);
        Assert.Equal(32, config.M0);
        Assert.Equal(200, config.EfConstruction);
        Assert.Equal(50, config.EfSearch);
        Assert.True(config.UseHeuristic);
        Assert.Equal(0, config.Seed);
    }

    [Fact]
    public void ML_ComputedCorrectly()
    {
        var config = HnswConfig.Default;
        double expected = 1.0 / Math.Log(16);

        Assert.Equal(expected, config.ML, 1e-10);
    }

    [Fact]
    public void Validate_DefaultConfig_DoesNotThrow()
    {
        HnswConfig.Default.Validate();
    }

    [Fact]
    public void Validate_MTooSmall_Throws()
    {
        var config = HnswConfig.Default with { M = 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void Validate_M0LessThanM_Throws()
    {
        var config = HnswConfig.Default with { M = 16, M0 = 8 };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void Validate_EfConstructionZero_Throws()
    {
        var config = HnswConfig.Default with { EfConstruction = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void Validate_EfSearchZero_Throws()
    {
        var config = HnswConfig.Default with { EfSearch = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void CustomConfig_OverridesDefaults()
    {
        var config = new HnswConfig
        {
            M = 32,
            M0 = 64,
            EfConstruction = 400,
            EfSearch = 100,
            UseHeuristic = false,
            Seed = 42
        };

        Assert.Equal(32, config.M);
        Assert.Equal(64, config.M0);
        Assert.Equal(400, config.EfConstruction);
        Assert.Equal(100, config.EfSearch);
        Assert.False(config.UseHeuristic);
        Assert.Equal(42, config.Seed);
    }
}
