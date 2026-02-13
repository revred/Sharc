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
/// Filter operators for the byte-level filter engine.
/// Operators evaluate directly against raw SQLite record bytes without materializing ColumnValue structs.
/// </summary>
public enum FilterOp : byte
{
    // ── Comparison (P1) ──

    /// <summary>Column value equals the filter value.</summary>
    Eq = 0,

    /// <summary>Column value does not equal the filter value.</summary>
    Neq = 1,

    /// <summary>Column value is less than the filter value.</summary>
    Lt = 2,

    /// <summary>Column value is less than or equal to the filter value.</summary>
    Lte = 3,

    /// <summary>Column value is greater than the filter value.</summary>
    Gt = 4,

    /// <summary>Column value is greater than or equal to the filter value.</summary>
    Gte = 5,

    /// <summary>Column value is between low and high (inclusive).</summary>
    Between = 6,

    // ── Null (P1) ──

    /// <summary>Column value is NULL (serial type == 0).</summary>
    IsNull = 10,

    /// <summary>Column value is not NULL (serial type != 0).</summary>
    IsNotNull = 11,

    // ── String (P1) ──

    /// <summary>UTF-8 column value starts with the given prefix.</summary>
    StartsWith = 20,

    /// <summary>UTF-8 column value ends with the given suffix.</summary>
    EndsWith = 21,

    /// <summary>UTF-8 column value contains the given substring.</summary>
    Contains = 22,

    // ── Set membership (P1) ──

    /// <summary>Column value is in the given set of values.</summary>
    In = 30,

    /// <summary>Column value is not in the given set of values.</summary>
    NotIn = 31,
}
