// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Sharc.Cache.OutputCaching.Tests;

public sealed class TenantEntitlementProviderTests
{
    [Fact]
    public void GetScope_HeaderPresent_ReturnsTenantScope()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "acme";
        var provider = new TenantEntitlementProvider(Accessor(context));

        Assert.Equal("tenant:acme", provider.GetScope());
    }

    [Fact]
    public void GetScope_CustomHeaderName_ReadsCorrectHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Org"] = "contoso";
        var provider = new TenantEntitlementProvider(Accessor(context), headerName: "X-Org");

        Assert.Equal("tenant:contoso", provider.GetScope());
    }

    [Fact]
    public void GetScope_HeaderMissing_RouteValuePresent_ReturnsTenantScope()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues = new RouteValueDictionary { ["tenantId"] = "acme" };
        var provider = new TenantEntitlementProvider(Accessor(context), routeValueKey: "tenantId");

        Assert.Equal("tenant:acme", provider.GetScope());
    }

    [Fact]
    public void GetScope_HeaderMissing_RouteValueMissing_ClaimPresent_ReturnsTenantScope()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "acme")], "Test"));
        var provider = new TenantEntitlementProvider(
            Accessor(context), routeValueKey: "tenantId", claimType: "tenant_id");

        Assert.Equal("tenant:acme", provider.GetScope());
    }

    [Fact]
    public void GetScope_HeaderTakesPrecedenceOverRouteValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "from-header";
        context.Request.RouteValues = new RouteValueDictionary { ["tenantId"] = "from-route" };
        var provider = new TenantEntitlementProvider(Accessor(context), routeValueKey: "tenantId");

        Assert.Equal("tenant:from-header", provider.GetScope());
    }

    [Fact]
    public void GetScope_RouteValueTakesPrecedenceOverClaim()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues = new RouteValueDictionary { ["tenantId"] = "from-route" };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "from-claim")], "Test"));
        var provider = new TenantEntitlementProvider(
            Accessor(context), routeValueKey: "tenantId", claimType: "tenant_id");

        Assert.Equal("tenant:from-route", provider.GetScope());
    }

    [Fact]
    public void GetScope_NothingPresent_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        var provider = new TenantEntitlementProvider(
            Accessor(context), routeValueKey: "tenantId", claimType: "tenant_id");

        Assert.Null(provider.GetScope());
    }

    [Fact]
    public void GetScope_NoHttpContext_ReturnsNull()
    {
        var provider = new TenantEntitlementProvider(Accessor(null));

        Assert.Null(provider.GetScope());
    }

    [Fact]
    public void GetScope_EmptyHeader_FallsThrough()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "";
        context.Request.RouteValues = new RouteValueDictionary { ["tenantId"] = "from-route" };
        var provider = new TenantEntitlementProvider(Accessor(context), routeValueKey: "tenantId");

        Assert.Equal("tenant:from-route", provider.GetScope());
    }

    [Fact]
    public void Constructor_NullAccessor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TenantEntitlementProvider(null!));
    }

    private static FakeHttpContextAccessor Accessor(HttpContext? context) => new(context);

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public FakeHttpContextAccessor(HttpContext? context) => HttpContext = context;
    }
}
