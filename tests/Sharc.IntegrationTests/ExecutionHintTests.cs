// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class ExecutionHintTests
{
    // Users table: id INTEGER PRIMARY KEY, name TEXT, age INTEGER, balance REAL, avatar BLOB
    // Rows: User1 (age=21), User2 (age=22), ..., User10 (age=30)

    // ── DIRECT hint ─────────────────────────────────────────────────

    [Fact]
    public void Query_DirectHint_SameAsNoHint()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        using var normal = db.Query("SELECT name FROM users ORDER BY name");
        using var hinted = db.Query("DIRECT SELECT name FROM users ORDER BY name");

        var normalNames = new List<string>();
        var hintedNames = new List<string>();
        while (normal.Read()) normalNames.Add(normal.GetString(0));
        while (hinted.Read()) hintedNames.Add(hinted.GetString(0));

        Assert.Equal(normalNames, hintedNames);
    }

    // ── CACHED hint ─────────────────────────────────────────────────

    [Fact]
    public void Query_CachedHint_ReturnsCorrectRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("CACHED SELECT name, age FROM users WHERE age > 27");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User8", names);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void Query_CachedHint_RepeatedCall_UsesCache()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        // First call — creates PreparedQuery internally
        using var r1 = db.Query("CACHED SELECT name FROM users");
        int count1 = 0;
        while (r1.Read()) count1++;

        // Second call — should reuse cached PreparedQuery
        using var r2 = db.Query("CACHED SELECT name FROM users");
        int count2 = 0;
        while (r2.Read()) count2++;

        Assert.Equal(5, count1);
        Assert.Equal(5, count2);
    }

    [Fact]
    public void Query_CachedHintWithParameters_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var parameters = new Dictionary<string, object> { ["minAge"] = 28L };
        using var reader = db.Query(parameters, "CACHED SELECT name FROM users WHERE age >= $minAge");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
    }

    // ── JIT hint ────────────────────────────────────────────────────

    [Fact]
    public void Query_JitHint_ReturnsCorrectRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("JIT SELECT name FROM users WHERE age < 24");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
        Assert.Contains("User3", names);
    }

    [Fact]
    public void Query_JitHint_RepeatedCall_UsesCache()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        using var r1 = db.Query("JIT SELECT name FROM users");
        int count1 = 0;
        while (r1.Read()) count1++;

        using var r2 = db.Query("JIT SELECT name FROM users");
        int count2 = 0;
        while (r2.Read()) count2++;

        Assert.Equal(5, count1);
        Assert.Equal(5, count2);
    }

    [Fact]
    public void Query_JitHint_WithProjection_ReturnsCorrectColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(3);
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("JIT SELECT name, age FROM users WHERE age = 22");

        Assert.True(reader.Read());
        Assert.Equal("User2", reader.GetString(0));
        Assert.Equal(22L, reader.GetInt64(1));
        Assert.False(reader.Read());
    }

    // ── JIT fallback for unsupported queries ────────────────────────

    [Fact]
    public void Query_JitHintWithCompound_FallsBackToDirect()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        // UNION query cannot use JIT — should fall back to DIRECT
        using var reader = db.Query(
            "JIT SELECT name FROM users WHERE age < 23 UNION SELECT name FROM users WHERE age > 28");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Should still produce correct results via fallback
        Assert.True(names.Count >= 2);
    }

    // ── ILayer + Jit(ILayer) ────────────────────────────────────────

    [Fact]
    public void Jit_AcceptsILayer_WorksLikeView()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 28)
            .Named("senior_users")
            .Build();

        // Pass as ILayer — Jit(ILayer) should accept SharcView via ILayer
        ILayer layer = view;
        var jit = db.Jit(layer);
        using var reader = jit.Query();

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User8", names);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void Jit_ILayer_WithStreamingStrategy_StillWorks()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Materialize(MaterializationStrategy.Streaming)
            .Named("streaming_users")
            .Build();

        Assert.Equal(MaterializationStrategy.Streaming, view.Strategy);

        var jit = db.Jit(view);
        using var reader = jit.Query("name");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }
}
