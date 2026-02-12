/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Core;

/// <summary>
/// Reads SQLite b-tree structures, supporting sequential table scans.
/// </summary>
public interface IBTreeReader
{
    /// <summary>
    /// Creates a cursor for iterating over all cells in a table b-tree.
    /// </summary>
    /// <param name="rootPage">The root page number of the table b-tree.</param>
    /// <returns>A cursor positioned before the first cell.</returns>
    IBTreeCursor CreateCursor(uint rootPage);
}

/// <summary>
/// Forward-only cursor over b-tree leaf cells.
/// </summary>
public interface IBTreeCursor : IDisposable
{
    /// <summary>
    /// Advances to the next cell.
    /// </summary>
    /// <returns>True if a cell is available; false at end of tree.</returns>
    bool MoveNext();

    /// <summary>
    /// Gets the rowid of the current cell (table b-trees only).
    /// </summary>
    long RowId { get; }

    /// <summary>
    /// Gets the payload data of the current cell.
    /// May involve reading overflow pages. Valid until <see cref="MoveNext"/> is called.
    /// </summary>
    ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// Gets the total payload size in bytes (including overflow).
    /// </summary>
    int PayloadSize { get; }
}
