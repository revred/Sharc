// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Tests;

public sealed class SharcEntitlementContextTests
{
    [Fact]
    public void Constructor_WithTags_StoresTags()
    {
        var ctx = new SharcEntitlementContext("tenant:acme", "role:admin");
        Assert.Equal(2, ctx.Tags.Count);
    }

    [Fact]
    public void HasEntitlement_ExistingTag_ReturnsTrue()
    {
        var ctx = new SharcEntitlementContext("tenant:acme", "role:admin");
        Assert.True(ctx.HasEntitlement("tenant:acme"));
        Assert.True(ctx.HasEntitlement("role:admin"));
    }

    [Fact]
    public void HasEntitlement_MissingTag_ReturnsFalse()
    {
        var ctx = new SharcEntitlementContext("tenant:acme");
        Assert.False(ctx.HasEntitlement("role:admin"));
    }

    [Fact]
    public void IsEmpty_NoTags_ReturnsTrue()
    {
        var ctx = new SharcEntitlementContext();
        Assert.True(ctx.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithTags_ReturnsFalse()
    {
        var ctx = new SharcEntitlementContext("tenant:acme");
        Assert.False(ctx.IsEmpty);
    }

    [Fact]
    public void Constructor_DuplicateTags_Deduplicates()
    {
        var ctx = new SharcEntitlementContext("tenant:acme", "tenant:acme", "role:admin");
        Assert.Equal(2, ctx.Tags.Count);
    }
}
