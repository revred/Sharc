/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.IntegrationTests.Helpers;
using Xunit;

#pragma warning disable CA1859 // Intentionally testing interface polymorphism

namespace Sharc.IntegrationTests;

public sealed class IPreparedReaderTests
{
    // ─── Type Checks ──────────────────────────────────────────────

    [Fact]
    public void PreparedReader_ImplementsIPreparedReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        Assert.IsAssignableFrom<IPreparedReader>(prepared);
    }

    [Fact]
    public void PreparedQuery_ImplementsIPreparedReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.Prepare("SELECT name FROM users");

        Assert.IsAssignableFrom<IPreparedReader>(prepared);
    }

    // ─── Execute via Interface ────────────────────────────────────

    [Fact]
    public void Execute_OnPreparedReader_ReturnsWorkingReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        IPreparedReader iface = prepared;
        using var reader = iface.Execute();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Execute_OnPreparedQuery_ReturnsWorkingReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.Prepare("SELECT name FROM users");

        IPreparedReader iface = prepared;
        using var reader = iface.Execute();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Execute_OnPreparedReader_MatchesCreateReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        // Collect via CreateReader
        var reader1 = prepared.CreateReader();
        Assert.True(reader1.Seek(5));
        var expectedName = reader1.GetString(1);
        reader1.Dispose();

        // Collect via Execute (interface)
        IPreparedReader iface = prepared;
        var reader2 = iface.Execute();
        Assert.True(reader2.Seek(5));
        var actualName = reader2.GetString(1);
        reader2.Dispose();

        Assert.Equal(expectedName, actualName);
    }

    // ─── Polymorphic Usage ────────────────────────────────────────

    [Fact]
    public void Polymorphic_Execute_WorksForBothTypes()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var preparedReader = db.PrepareReader("users");
        using var preparedQuery = db.Prepare("SELECT name FROM users");

        Assert.Equal(10, CountRows(preparedReader));
        Assert.Equal(10, CountRows(preparedQuery));
    }

    [Fact]
    public void Dispose_ViaInterface_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        IPreparedReader iface = db.PrepareReader("users");
        iface.Dispose(); // should not throw
        iface.Dispose(); // double dispose should not throw
    }

    // ─── Reuse Through Interface ──────────────────────────────────

    [Fact]
    public void Execute_MultipleCalls_ReturnsSameInstance()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        IPreparedReader iface = prepared;
        var reader1 = iface.Execute();
        reader1.Dispose();
        var reader2 = iface.Execute();

        Assert.Same(reader1, reader2);
    }

    private static int CountRows(IPreparedReader prepared)
    {
        using var reader = prepared.Execute();
        int count = 0;
        while (reader.Read())
            count++;
        return count;
    }
}
