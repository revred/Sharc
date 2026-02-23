/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

#pragma warning disable CA1859 // Intentionally testing interface polymorphism

namespace Sharc.IntegrationTests;

public sealed class JitQueryInterfaceTests : IDisposable
{
    private readonly string _dbPath;

    public JitQueryInterfaceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"jit_iface_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); }
        catch { /* best-effort cleanup */ }
    }

    // ─── IPreparedReader ─────────────────────────────────────────

    [Fact]
    public void JitQuery_ImplementsIPreparedReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var jit = db.Jit("users");

        Assert.IsAssignableFrom<IPreparedReader>(jit);
    }

    [Fact]
    public void Execute_ViaIPreparedReader_ReturnsWorkingReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var jit = db.Jit("users");

        IPreparedReader iface = jit;
        using var reader = iface.Execute();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Execute_MatchesQuery_Results()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var jit = db.Jit("users");

        // Query() path
        var queryReader = jit.Query();
        Assert.True(queryReader.Seek(5));
        var expected = queryReader.GetString(1);
        queryReader.Dispose();

        // Execute() (interface) path
        IPreparedReader iface = jit;
        var execReader = iface.Execute();
        Assert.True(execReader.Seek(5));
        var actual = execReader.GetString(1);
        execReader.Dispose();

        Assert.Equal(expected, actual);
    }

    // ─── IPreparedWriter ─────────────────────────────────────────

    [Fact]
    public void JitQuery_ImplementsIPreparedWriter()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);
        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var jit = db.Jit("users");

        Assert.IsAssignableFrom<IPreparedWriter>(jit);
    }

    [Fact]
    public void Insert_ViaIPreparedWriter_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);
        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var jit = db.Jit("users");

        IPreparedWriter iface = jit;
        long rowId = iface.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("JitUser")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        Assert.True(rowId > 0);
    }

    [Fact]
    public void Delete_ViaIPreparedWriter_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        File.WriteAllBytes(_dbPath, data);
        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var jit = db.Jit("users");

        IPreparedWriter iface = jit;
        Assert.True(iface.Delete(3));
    }

    [Fact]
    public void Update_ViaIPreparedWriter_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        File.WriteAllBytes(_dbPath, data);
        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var jit = db.Jit("users");

        IPreparedWriter iface = jit;
        Assert.True(iface.Update(1,
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("IfaceUpdate")),
            ColumnValue.FromInt64(2, 99),
            ColumnValue.FromDouble(99.0),
            ColumnValue.Null()));
    }

    // ─── Polymorphic across both PreparedWriter and JitQuery ─────

    [Fact]
    public void Polymorphic_CountRows_WorksForPreparedReaderAndJitQuery()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var preparedReader = db.PrepareReader("users");
        using var jit = db.Jit("users");

        Assert.Equal(10, CountRows(preparedReader));
        Assert.Equal(10, CountRows(jit));
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
