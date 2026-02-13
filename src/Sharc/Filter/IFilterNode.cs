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

namespace Sharc;

/// <summary>
/// Evaluates a filter predicate against a raw SQLite record.
/// Implementations must not allocate on the evaluation path.
/// </summary>
internal interface IFilterNode
{
    /// <summary>
    /// Evaluates the filter against raw record data.
    /// </summary>
    /// <param name="payload">Raw SQLite record bytes (header + body).</param>
    /// <param name="serialTypes">Pre-parsed serial types from record header.</param>
    /// <param name="bodyOffset">Byte offset where record body begins.</param>
    /// <param name="rowId">B-tree rowid of the current row.</param>
    /// <returns>True if the row matches the filter.</returns>
    bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                  int bodyOffset, long rowId);
}
