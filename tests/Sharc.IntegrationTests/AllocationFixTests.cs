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

using Sharc;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests for allocation reduction fixes (#2–#5 from CompetitiveAnalysis.md).
/// </summary>
public class AllocationFixTests
{
    // --- Fix #2: Lazy Schema Initialization ---

    [Fact]
    public void OpenMemory_InfoAccessible_BeforeSchemaAccess()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(bytes);

        // Info should be available without triggering schema parse
        var info = db.Info;
        Assert.True(info.PageSize > 0);
        Assert.True(info.PageCount > 0);
        Assert.Equal(1, (int)info.TextEncoding);
    }

    [Fact]
    public void OpenMemory_SchemaStillCorrect_WhenAccessedLazily()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(bytes);

        // Access Info first (should not trigger schema)
        _ = db.Info;

        // Now access Schema (should trigger lazy parse)
        var schema = db.Schema;
        Assert.Single(schema.Tables);
        Assert.Equal("users", schema.Tables[0].Name);
        Assert.Equal(5, schema.Tables[0].Columns.Count);
    }

    [Fact]
    public void OpenMemory_LazySchema_CreateReaderStillWorks()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(bytes);

        // Don't access Schema directly — let CreateReader trigger it
        using var reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void OpenMemory_LazySchema_ReducesAllocation()
    {
        var bytes = TestDatabaseFactory.CreateMultiTableDatabase();

        // Measure allocation for Info-only access (schema NOT parsed)
        var before = GC.GetAllocatedBytesForCurrentThread();
        using (var db = SharcDatabase.OpenMemory(bytes))
        {
            _ = db.Info;
        }
        var infoOnlyAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

        // Measure allocation for Schema access (schema IS parsed)
        before = GC.GetAllocatedBytesForCurrentThread();
        using (var db = SharcDatabase.OpenMemory(bytes))
        {
            _ = db.Schema.Tables;
        }
        var schemaAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

        // Info-only should allocate less than schema access
        Assert.True(infoOnlyAlloc < schemaAlloc,
            $"Info-only ({infoOnlyAlloc} B) should allocate less than schema ({schemaAlloc} B)");
    }

    // --- Fix #4: ColumnValue[] Buffer Pooling (transparent — existing tests verify correctness) ---

    [Fact]
    public void CreateReader_MultipleReaders_BufferPoolingDoesNotCorruptData()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(bytes);

        // Create and dispose many readers — pooled buffers should be returned cleanly
        for (int i = 0; i < 20; i++)
        {
            using var reader = db.CreateReader("users");
            Assert.True(reader.Read());
            var name = reader.GetString(1);
            Assert.StartsWith("User", name);
        }
    }

    // --- Fix #5: GetUtf8Span() Zero-Alloc String API ---

    [Fact]
    public void GetUtf8Span_ReturnsCorrectUtf8Bytes()
    {
        var bytes = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(bytes);
        using var reader = db.CreateReader("all_types");

        Assert.True(reader.Read()); // Row 1
        var utf8Span = reader.GetUtf8Span(3); // text_val = "Hello, Sharc!"
        var expected = System.Text.Encoding.UTF8.GetBytes("Hello, Sharc!");
        Assert.Equal(expected, utf8Span.ToArray());
    }

    [Fact]
    public void GetUtf8Span_MatchesGetString()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(bytes);
        using var reader = db.CreateReader("users");

        while (reader.Read())
        {
            var fromString = reader.GetString(1); // name column
            var fromUtf8 = System.Text.Encoding.UTF8.GetString(reader.GetUtf8Span(1));
            Assert.Equal(fromString, fromUtf8);
        }
    }

    [Fact]
    public void GetUtf8Span_WithProjection_ReturnsCorrectBytes()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(bytes);
        using var reader = db.CreateReader("users", "name", "age");

        Assert.True(reader.Read());
        var utf8 = reader.GetUtf8Span(0); // projected name
        var name = System.Text.Encoding.UTF8.GetString(utf8);
        Assert.StartsWith("User", name);
    }

    // --- Fix #2 + BTreeReader: Shared reader still works with lazy schema ---

    [Fact]
    public void BTreeReader_AccessibleWithoutSchemaAccess()
    {
        var bytes = TestDatabaseFactory.CreateUsersDatabase();
        using var db = SharcDatabase.OpenMemory(bytes);

        // BTreeReader should be usable without ever touching Schema
        var btr = db.BTreeReader;
        Assert.NotNull(btr);
    }
}
