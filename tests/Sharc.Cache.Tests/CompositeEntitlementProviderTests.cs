// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CompositeEntitlementProviderTests
{
    [Fact]
    public void GetScope_SingleProvider_ReturnsThatScope()
    {
        var inner = new FakeProvider("tenant:acme");
        var composite = new CompositeEntitlementProvider(inner);

        Assert.Equal("tenant:acme", composite.GetScope());
    }

    [Fact]
    public void GetScope_MultipleProviders_CombinesWithPipe()
    {
        var a = new FakeProvider("tenant:acme");
        var b = new FakeProvider("role:admin");
        var composite = new CompositeEntitlementProvider(a, b);

        Assert.Equal("tenant:acme|role:admin", composite.GetScope());
    }

    [Fact]
    public void GetScope_ThreeProviders_CombinesAll()
    {
        var a = new FakeProvider("tenant:acme");
        var b = new FakeProvider("role:admin");
        var c = new FakeProvider("user:42");
        var composite = new CompositeEntitlementProvider(a, b, c);

        Assert.Equal("tenant:acme|role:admin|user:42", composite.GetScope());
    }

    [Fact]
    public void GetScope_AllReturnNull_ReturnsNull()
    {
        var a = new FakeProvider(null);
        var b = new FakeProvider(null);
        var composite = new CompositeEntitlementProvider(a, b);

        Assert.Null(composite.GetScope());
    }

    [Fact]
    public void GetScope_SomeReturnNull_SkipsNulls()
    {
        var a = new FakeProvider("tenant:acme");
        var b = new FakeProvider(null);
        var c = new FakeProvider("role:admin");
        var composite = new CompositeEntitlementProvider(a, b, c);

        Assert.Equal("tenant:acme|role:admin", composite.GetScope());
    }

    [Fact]
    public void GetScope_OnlyOneNonNull_ReturnsThatScope()
    {
        var a = new FakeProvider(null);
        var b = new FakeProvider("tenant:acme");
        var c = new FakeProvider(null);
        var composite = new CompositeEntitlementProvider(a, b, c);

        Assert.Equal("tenant:acme", composite.GetScope());
    }

    [Fact]
    public void Constructor_EmptyProviders_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CompositeEntitlementProvider());
    }

    [Fact]
    public void Constructor_NullProviders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeEntitlementProvider(null!));
    }

    [Fact]
    public void GetScope_DynamicProviders_ReflectsCurrentState()
    {
        var mutable = new FakeProvider("tenant:acme");
        var composite = new CompositeEntitlementProvider(mutable);

        Assert.Equal("tenant:acme", composite.GetScope());

        mutable.Scope = "tenant:contoso";
        Assert.Equal("tenant:contoso", composite.GetScope());

        mutable.Scope = null;
        Assert.Null(composite.GetScope());
    }

    private sealed class FakeProvider : IEntitlementProvider
    {
        public string? Scope { get; set; }
        public FakeProvider(string? scope) => Scope = scope;
        public string? GetScope() => Scope;
    }
}
