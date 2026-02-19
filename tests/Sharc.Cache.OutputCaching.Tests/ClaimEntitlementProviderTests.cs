// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Sharc.Cache.OutputCaching.Tests;

public sealed class ClaimEntitlementProviderTests
{
    [Fact]
    public void GetScope_DefaultClaimPresent_ReturnsClaimValue()
    {
        var accessor = CreateAccessor(new Claim("tenant_id", "acme"));
        var provider = new ClaimEntitlementProvider(accessor);

        Assert.Equal("acme", provider.GetScope());
    }

    [Fact]
    public void GetScope_CustomClaimType_ReturnsClaimValue()
    {
        var accessor = CreateAccessor(new Claim("org_id", "contoso"));
        var provider = new ClaimEntitlementProvider(accessor, claimType: "org_id");

        Assert.Equal("contoso", provider.GetScope());
    }

    [Fact]
    public void GetScope_WithPrefix_ReturnsPrefixedValue()
    {
        var accessor = CreateAccessor(new Claim("tenant_id", "acme"));
        var provider = new ClaimEntitlementProvider(accessor, scopePrefix: "tenant:");

        Assert.Equal("tenant:acme", provider.GetScope());
    }

    [Fact]
    public void GetScope_ClaimMissing_ReturnsNull()
    {
        var accessor = CreateAccessor(new Claim("other_claim", "value"));
        var provider = new ClaimEntitlementProvider(accessor);

        Assert.Null(provider.GetScope());
    }

    [Fact]
    public void GetScope_Unauthenticated_ReturnsNull()
    {
        var accessor = CreateAccessor();
        var provider = new ClaimEntitlementProvider(accessor);

        Assert.Null(provider.GetScope());
    }

    [Fact]
    public void GetScope_NoHttpContext_ReturnsNull()
    {
        var accessor = new FakeHttpContextAccessor(null);
        var provider = new ClaimEntitlementProvider(accessor);

        Assert.Null(provider.GetScope());
    }

    [Fact]
    public void Constructor_NullAccessor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ClaimEntitlementProvider(null!));
    }

    [Fact]
    public void Constructor_NullClaimType_Throws()
    {
        var accessor = CreateAccessor();
        Assert.Throws<ArgumentNullException>(() => new ClaimEntitlementProvider(accessor, claimType: null!));
    }

    private static FakeHttpContextAccessor CreateAccessor(params Claim[] claims)
    {
        var context = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            var identity = new ClaimsIdentity(claims, "Test");
            context.User = new ClaimsPrincipal(identity);
        }
        return new FakeHttpContextAccessor(context);
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public FakeHttpContextAccessor(HttpContext? context) => HttpContext = context;
    }
}
