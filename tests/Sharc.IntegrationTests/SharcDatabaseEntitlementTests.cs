// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public class SharcDatabaseEntitlementTests
{
    private static AgentInfo MakeAgent(string readScope) =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "*", readScope, 0, 0, "", false, Array.Empty<byte>());

    [Fact]
    public void Query_EntitledAgent_ReturnsData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        using var reader = db.Query("SELECT * FROM users WHERE age = 25", agent);
        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
    }

    [Fact]
    public void Query_DeniedAgent_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("orders.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT * FROM users", agent));
    }

    [Fact]
    public void Query_ColumnRestriction_Enforced()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.name");

        // Allowed: query only the permitted column
        using var reader = db.Query("SELECT name FROM users", agent);
        Assert.True(reader.Read());

        // Denied: query a column not in scope
        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT name, age FROM users", agent));
    }

    [Fact]
    public void Query_SystemTable_RequiresScope()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT * FROM sqlite_schema", agent));
    }

    // ─── Combined Parameters + Agent ─────────────────────────────

    [Fact]
    public void Query_AgentWithParameters_BindsAndEnforces()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        using var reader = db.Query(
            new Dictionary<string, object> { ["targetAge"] = 25L },
            "SELECT * FROM users WHERE age = $targetAge",
            agent);

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_AgentWithParameters_DeniedScope_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("orders.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query(
                new Dictionary<string, object> { ["targetAge"] = 25L },
                "SELECT * FROM users WHERE age = $targetAge",
                agent));
    }

    [Fact]
    public void Query_AgentWithMissingParameter_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        Assert.Throws<ArgumentException>(() =>
            db.Query(
                new Dictionary<string, object>(),
                "SELECT * FROM users WHERE age = $targetAge",
                agent));
    }

    [Fact]
    public void Query_AgentParameterized_CachesAndReuses()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");
        const string sql = "SELECT * FROM users WHERE age = $age";

        using (var reader = db.Query(
            new Dictionary<string, object> { ["age"] = 25L }, sql, agent))
        {
            Assert.True(reader.Read());
            Assert.Equal("User5", reader.GetString(1));
        }

        using (var reader = db.Query(
            new Dictionary<string, object> { ["age"] = 28L }, sql, agent))
        {
            Assert.True(reader.Read());
            Assert.Equal("User8", reader.GetString(1));
        }
    }

    [Fact]
    public void Query_AgentWithOrderByAndParams_SortsCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        using var reader = db.Query(
            new Dictionary<string, object> { ["minAge"] = 25L },
            "SELECT * FROM users WHERE age > $minAge ORDER BY age DESC",
            agent);

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(5, ages.Count);
        Assert.Equal(30L, ages[0]);
        Assert.Equal(26L, ages[4]);
    }

    // ─── SEC-001: Entitlement bypass on hinted query paths ─────

    [Fact]
    public void Query_CachedHint_DeniedAgent_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("orders.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("CACHED SELECT * FROM users", agent));
    }

    [Fact]
    public void Query_JitHint_DeniedAgent_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("orders.*");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("JIT SELECT * FROM users", agent));
    }

    [Fact]
    public void Query_CachedHint_EntitledAgent_Succeeds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        using var reader = db.Query("CACHED SELECT name FROM users WHERE age = 25", agent);
        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(0));
    }

    [Fact]
    public void Query_CachedHint_ColumnRestricted_DeniedColumn_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("CACHED SELECT name, age FROM users", agent));
    }

    // ─── SEC-002: Column-level entitlement for wildcard/aggregate ─

    [Fact]
    public void Query_SelectStar_ColumnRestricted_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT * FROM users", agent));
    }

    [Fact]
    public void Query_SelectStar_TableWideScope_Succeeds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var agent = MakeAgent("users.*");

        using var reader = db.Query("SELECT * FROM users WHERE age = 25", agent);
        Assert.True(reader.Read());
    }

    // ─── SEC-002: Aggregate column entitlement enforcement ────────

    [Fact]
    public void Query_Aggregate_DeniedSourceColumn_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        // Agent can read name but NOT balance
        var agent = MakeAgent("users.name,users.age");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT SUM(balance) FROM users", agent));
    }

    [Fact]
    public void Query_Aggregate_AllowedSourceColumn_Succeeds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        // Aggregate produces a synthetic column (COUNT(age)), so table-wide scope is needed
        var agent = MakeAgent("users.*");

        using var reader = db.Query("SELECT COUNT(age) FROM users", agent);
        Assert.True(reader.Read());
    }

    // ─── SEC-002: CASE expression column entitlement enforcement ──

    [Fact]
    public void Query_CaseExpression_DeniedSourceColumn_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        // Agent can read name but NOT age
        var agent = MakeAgent("users.name");

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query("SELECT CASE WHEN age >= 25 THEN 'senior' ELSE 'junior' END FROM users", agent));
    }

    [Fact]
    public void Query_CaseExpression_AllowedSourceColumn_Succeeds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        // CASE produces a synthetic column (_case_0), so table-wide scope is needed
        var agent = MakeAgent("users.*");

        using var reader = db.Query("SELECT CASE WHEN age >= 25 THEN 'senior' ELSE 'junior' END FROM users", agent);
        Assert.True(reader.Read());
    }
}
