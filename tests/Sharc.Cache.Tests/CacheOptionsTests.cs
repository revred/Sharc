// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CacheOptionsTests
{
    [Fact]
    public void Defaults_MaxCacheSize_Is256MB()
    {
        var options = new CacheOptions();
        Assert.Equal(256L * 1024 * 1024, options.MaxCacheSize);
    }

    [Fact]
    public void Defaults_SweepInterval_Is60Seconds()
    {
        var options = new CacheOptions();
        Assert.Equal(TimeSpan.FromSeconds(60), options.SweepInterval);
    }

    [Fact]
    public void Defaults_MaxEntries_IsZero()
    {
        var options = new CacheOptions();
        Assert.Equal(0, options.MaxEntries);
    }

    [Fact]
    public void Defaults_TimeProvider_IsSystem()
    {
        var options = new CacheOptions();
        Assert.Same(TimeProvider.System, options.TimeProvider);
    }
}
