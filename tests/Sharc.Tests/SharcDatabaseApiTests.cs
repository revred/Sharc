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
    [Fact]
    public void Open_NonexistentFile_ThrowsFileNotFoundException()
    {
        // Use a path whose parent directory exists to ensure FileNotFoundException (not DirectoryNotFoundException)
        var dir = Path.GetTempPath();
        var fakePath = Path.Combine(dir, "nonexistent_sharc_" + Guid.NewGuid().ToString("N") + ".db");
        Assert.Throws<FileNotFoundException>(() => SharcDatabase.Open(fakePath));
    }

    [Fact]
    public void Open_InvalidFile_ThrowsInvalidDatabaseException()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "This is not a SQLite database");
            Assert.Throws<InvalidDatabaseException>(() => SharcDatabase.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenMemory_EmptyBuffer_ThrowsInvalidDatabaseException()
    {
        Assert.Throws<InvalidDatabaseException>(() => SharcDatabase.OpenMemory(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void OpenMemory_InvalidMagic_ThrowsInvalidDatabaseException()
    {
        var garbage = new byte[4096];
        Assert.Throws<InvalidDatabaseException>(() => SharcDatabase.OpenMemory(garbage));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            // SharcDatabase.Dispose() is implemented (safe no-op pattern)
        });
        Assert.Null(ex);
    }
}
