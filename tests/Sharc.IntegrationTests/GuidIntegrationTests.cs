/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.Primitives;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for GUID native type support through the full Sharc stack:
/// SQLite (via Microsoft.Data.Sqlite) → Sharc reader → GuidCodec → Guid.
/// </summary>
public class GuidIntegrationTests
{
    [Fact]
    public void GetGuid_SingleRow_ReturnsCorrectGuid()
    {
        var expectedGuid = Guid.NewGuid();
        var guidBytes = new byte[16];
        GuidCodec.Encode(expectedGuid, guidBytes);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, owner_id BLOB)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO items (owner_id) VALUES ($guid)";
            cmd.Parameters.AddWithValue("$guid", guidBytes);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items");

        Assert.True(reader.Read());
        var actual = reader.GetGuid(1);
        Assert.Equal(expectedGuid, actual);
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetGuid_MultipleRows_AllRoundTrip()
    {
        var guids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE refs (id INTEGER PRIMARY KEY, ref_id BLOB)";
            cmd.ExecuteNonQuery();

            foreach (var guid in guids)
            {
                var bytes = new byte[16];
                GuidCodec.Encode(guid, bytes);
                cmd.CommandText = "INSERT INTO refs (ref_id) VALUES ($g)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$g", bytes);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("refs");

        for (int i = 0; i < guids.Length; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(guids[i], reader.GetGuid(1));
        }
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetGuid_EmptyGuid_RoundTrips()
    {
        var guidBytes = new byte[16]; // Guid.Empty = all zeros

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, g BLOB)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO t (g) VALUES ($g)";
            cmd.Parameters.AddWithValue("$g", guidBytes);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("t");

        Assert.True(reader.Read());
        Assert.Equal(Guid.Empty, reader.GetGuid(1));
    }

    [Fact]
    public void SchemaParser_GuidDeclaredType_IsGuidColumnTrue()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            // SQLite stores declared type as-is in sqlite_master
            cmd.CommandText = "CREATE TABLE entities (id INTEGER PRIMARY KEY, entity_guid GUID NOT NULL, ref_uuid UUID)";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        var table = db.Schema.GetTable("entities");

        Assert.Equal(3, table.Columns.Count);
        Assert.False(table.Columns[0].IsGuidColumn); // id INTEGER
        Assert.True(table.Columns[1].IsGuidColumn);  // entity_guid GUID
        Assert.True(table.Columns[2].IsGuidColumn);  // ref_uuid UUID
    }

    [Fact]
    public void GetGuid_WithGuidDeclaredType_RoundTrips()
    {
        var expectedGuid = Guid.NewGuid();
        var guidBytes = new byte[16];
        GuidCodec.Encode(expectedGuid, guidBytes);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            // Use GUID as declared type — SQLite stores it as BLOB affinity
            cmd.CommandText = "CREATE TABLE docs (id INTEGER PRIMARY KEY, doc_guid GUID)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO docs (doc_guid) VALUES ($g)";
            cmd.Parameters.AddWithValue("$g", guidBytes);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);

        // Verify schema recognizes GUID type
        var table = db.Schema.GetTable("docs");
        Assert.True(table.Columns[1].IsGuidColumn);

        // Verify data reads back correctly
        using var reader = db.CreateReader("docs");
        Assert.True(reader.Read());
        Assert.Equal(expectedGuid, reader.GetGuid(1));
    }
}
