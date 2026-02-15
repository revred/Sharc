// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class ScopeDescriptorTests
{
    [Fact]
    public void Parse_Star_IsUnrestricted()
    {
        var scope = ScopeDescriptor.Parse("*");
        Assert.True(scope.IsUnrestricted);
    }

    [Fact]
    public void Parse_SingleTable_AllowsAllColumns()
    {
        var scope = ScopeDescriptor.Parse("users.*");
        Assert.False(scope.IsUnrestricted);
        Assert.True(scope.CanReadTable("users"));
        Assert.True(scope.CanReadColumn("users", "name"));
        Assert.True(scope.CanReadColumn("users", "age"));
    }

    [Fact]
    public void Parse_SpecificColumns_AllowsOnlyThose()
    {
        var scope = ScopeDescriptor.Parse("users.name,users.email");
        Assert.True(scope.CanReadTable("users"));
        Assert.True(scope.CanReadColumn("users", "name"));
        Assert.True(scope.CanReadColumn("users", "email"));
        Assert.False(scope.CanReadColumn("users", "password"));
    }

    [Fact]
    public void Parse_WildcardPrefix_MatchesSystemTables()
    {
        var scope = ScopeDescriptor.Parse("_sharc_*.*");
        Assert.True(scope.CanReadTable("_sharc_agents"));
        Assert.True(scope.CanReadTable("_sharc_ledger"));
        Assert.False(scope.CanReadTable("users"));
    }

    [Fact]
    public void CanReadTable_Allowed_ReturnsTrue()
    {
        var scope = ScopeDescriptor.Parse("users.*,orders.*");
        Assert.True(scope.CanReadTable("users"));
        Assert.True(scope.CanReadTable("orders"));
    }

    [Fact]
    public void CanReadTable_Denied_ReturnsFalse()
    {
        var scope = ScopeDescriptor.Parse("users.*");
        Assert.False(scope.CanReadTable("orders"));
    }

    [Fact]
    public void CanReadColumn_Allowed_ReturnsTrue()
    {
        var scope = ScopeDescriptor.Parse("users.name");
        Assert.True(scope.CanReadColumn("users", "name"));
    }

    [Fact]
    public void CanReadColumn_Denied_ReturnsFalse()
    {
        var scope = ScopeDescriptor.Parse("users.name");
        Assert.False(scope.CanReadColumn("users", "email"));
    }

    [Fact]
    public void Parse_EmptyScope_DeniesAll()
    {
        var scope = ScopeDescriptor.Parse("");
        Assert.False(scope.IsUnrestricted);
        Assert.False(scope.CanReadTable("users"));
        Assert.False(scope.CanReadColumn("users", "name"));
    }

    [Fact]
    public void Parse_MultipleEntries_AllParsed()
    {
        var scope = ScopeDescriptor.Parse("users.*,orders.total,products.name");
        Assert.True(scope.CanReadTable("users"));
        Assert.True(scope.CanReadColumn("users", "anything"));
        Assert.True(scope.CanReadColumn("orders", "total"));
        Assert.False(scope.CanReadColumn("orders", "secret"));
        Assert.True(scope.CanReadColumn("products", "name"));
        Assert.False(scope.CanReadTable("admin"));
    }

    [Fact]
    public void Unrestricted_AllowsAnything()
    {
        var scope = ScopeDescriptor.Parse("*");
        Assert.True(scope.CanReadTable("anything"));
        Assert.True(scope.CanReadColumn("anything", "anything"));
    }

    [Fact]
    public void Parse_Whitespace_Trimmed()
    {
        var scope = ScopeDescriptor.Parse(" users.* , orders.* ");
        Assert.True(scope.CanReadTable("users"));
        Assert.True(scope.CanReadTable("orders"));
    }
}
