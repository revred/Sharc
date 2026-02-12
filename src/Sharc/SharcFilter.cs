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
/// Comparison operators for row filtering during table scans.
/// </summary>
public enum SharcOperator
{
    /// <summary>Column value equals the filter value.</summary>
    Equal,

    /// <summary>Column value does not equal the filter value.</summary>
    NotEqual,

    /// <summary>Column value is less than the filter value.</summary>
    LessThan,

    /// <summary>Column value is greater than the filter value.</summary>
    GreaterThan,

    /// <summary>Column value is less than or equal to the filter value.</summary>
    LessOrEqual,

    /// <summary>Column value is greater than or equal to the filter value.</summary>
    GreaterOrEqual
}

/// <summary>
/// Defines a single column filter condition for table scans.
/// Multiple filters are combined with AND semantics.
/// </summary>
/// <param name="ColumnName">The column to filter on (case-insensitive match).</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The value to compare against. Supported types: long, int, double, string, null.</param>
public sealed record SharcFilter(string ColumnName, SharcOperator Operator, object? Value);
