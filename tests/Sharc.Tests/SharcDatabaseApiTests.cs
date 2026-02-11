using FluentAssertions;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD RED-phase tests for SharcDatabase public API.
/// These define the expected behavior before implementation exists.
/// Skipped until SharcDatabase.Open/OpenMemory are implemented (Milestone 4+).
/// When that milestone begins, remove the Skip to go RED, then implement to go GREEN.
/// </summary>
public class SharcDatabaseTests
{
    [Fact(Skip = "TDD RED: SharcDatabase.Open not yet implemented (Milestone 4+)")]
    public void Open_NonexistentFile_ThrowsFileNotFoundException()
    {
        var act = () => SharcDatabase.Open("/nonexistent/path/to/database.db");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact(Skip = "TDD RED: SharcDatabase.Open not yet implemented (Milestone 4+)")]
    public void Open_InvalidFile_ThrowsInvalidDatabaseException()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "This is not a SQLite database");
            var act = () => SharcDatabase.Open(path);
            act.Should().Throw<InvalidDatabaseException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(Skip = "TDD RED: SharcDatabase.OpenMemory not yet implemented (Milestone 4+)")]
    public void OpenMemory_EmptyBuffer_ThrowsInvalidDatabaseException()
    {
        var act = () => SharcDatabase.OpenMemory(ReadOnlyMemory<byte>.Empty);
        act.Should().Throw<InvalidDatabaseException>();
    }

    [Fact(Skip = "TDD RED: SharcDatabase.OpenMemory not yet implemented (Milestone 4+)")]
    public void OpenMemory_InvalidMagic_ThrowsInvalidDatabaseException()
    {
        var garbage = new byte[4096];
        var act = () => SharcDatabase.OpenMemory(garbage);
        act.Should().Throw<InvalidDatabaseException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var act = () =>
        {
            // SharcDatabase.Dispose() is implemented (safe no-op pattern)
        };
        act.Should().NotThrow();
    }
}
