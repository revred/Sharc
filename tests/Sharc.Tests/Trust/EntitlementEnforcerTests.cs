// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class EntitlementEnforcerTests
{
    private static AgentInfo MakeAgent(string readScope) =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "*", readScope, 0, 0, "", false, Array.Empty<byte>());

    private static AgentInfo MakeWriteAgent(string writeScope) =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            writeScope, "*", 0, 0, "", false, Array.Empty<byte>());

    private static AgentInfo MakeTimedAgent(string readScope, long validityStart, long validityEnd) =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "*", readScope, validityStart, validityEnd, "", false, Array.Empty<byte>());

    [Fact]
    public void Enforce_Unrestricted_NoThrow()
    {
        var agent = MakeAgent("*");
        EntitlementEnforcer.Enforce(agent, "users", ["name", "age"]);
    }

    [Fact]
    public void Enforce_AllowedTable_NoThrow()
    {
        var agent = MakeAgent("users.*");
        EntitlementEnforcer.Enforce(agent, "users", ["name"]);
    }

    [Fact]
    public void Enforce_DeniedTable_Throws()
    {
        var agent = MakeAgent("orders.*");
        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.Enforce(agent, "users", null));
    }

    [Fact]
    public void Enforce_AllowedColumn_NoThrow()
    {
        var agent = MakeAgent("users.name,users.age");
        EntitlementEnforcer.Enforce(agent, "users", ["name", "age"]);
    }

    [Fact]
    public void Enforce_DeniedColumn_Throws()
    {
        var agent = MakeAgent("users.name");
        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.Enforce(agent, "users", ["name", "password"]));
    }

    [Fact]
    public void Enforce_NullColumns_ChecksTableOnly()
    {
        var agent = MakeAgent("users.*");
        EntitlementEnforcer.Enforce(agent, "users", null);
    }

    // ─── WriteScope Enforcement ─────────────────────────────────

    [Fact]
    public void EnforceWrite_Unrestricted_NoThrow()
    {
        var agent = MakeWriteAgent("*");
        EntitlementEnforcer.EnforceWrite(agent, "users", null);
    }

    [Fact]
    public void EnforceWrite_AllowedTable_NoThrow()
    {
        var agent = MakeWriteAgent("users.*");
        EntitlementEnforcer.EnforceWrite(agent, "users", null);
    }

    [Fact]
    public void EnforceWrite_DeniedTable_Throws()
    {
        var agent = MakeWriteAgent("orders.*");
        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.EnforceWrite(agent, "users", null));
    }

    [Fact]
    public void EnforceWrite_AllowedColumn_NoThrow()
    {
        var agent = MakeWriteAgent("users.name,users.age");
        EntitlementEnforcer.EnforceWrite(agent, "users", ["name", "age"]);
    }

    [Fact]
    public void EnforceWrite_DeniedColumn_Throws()
    {
        var agent = MakeWriteAgent("users.name");
        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.EnforceWrite(agent, "users", ["name", "password"]));
    }

    [Fact]
    public void EnforceWrite_NullColumns_ChecksTableOnly()
    {
        var agent = MakeWriteAgent("users.*");
        EntitlementEnforcer.EnforceWrite(agent, "users", null);
    }

    // ─── Agent Validity Enforcement ─────────────────────────────

    [Fact]
    public void Enforce_ExpiredAgent_Throws()
    {
        // Agent expired 1 hour ago
        long pastEnd = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var agent = MakeTimedAgent("*", 0, pastEnd);

        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.Enforce(agent, "users", null));
    }

    [Fact]
    public void Enforce_NotYetActiveAgent_Throws()
    {
        // Agent starts 1 hour from now
        long futureStart = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var agent = MakeTimedAgent("*", futureStart, 0);

        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.Enforce(agent, "users", null));
    }

    [Fact]
    public void Enforce_ZeroValidityDates_NoCheck()
    {
        // 0 means no restriction — default for agents
        var agent = MakeTimedAgent("*", 0, 0);
        EntitlementEnforcer.Enforce(agent, "users", null);
    }

    [Fact]
    public void EnforceWrite_ExpiredAgent_Throws()
    {
        long pastEnd = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var agent = new AgentInfo("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "*", "*", 0, pastEnd, "", false, Array.Empty<byte>());

        Assert.Throws<UnauthorizedAccessException>(() =>
            EntitlementEnforcer.EnforceWrite(agent, "users", null));
    }
}
