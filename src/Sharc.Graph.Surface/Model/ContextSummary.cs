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

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Graph.Model;

/// <summary>
/// A summarized context view suitable for LLM consumption.
/// </summary>
public sealed class ContextSummary
{
    /// <summary>The root concept ID requested.</summary>
    public Guid RootId { get; }
    
    /// <summary>Generated summary text (if any).</summary>
    public string SummaryText { get; }
    
    /// <summary>Estimated token usage of this summary.</summary>
    public int TokenCount { get; }
    
    /// <summary>The records included in this context.</summary>
    public IReadOnlyList<GraphRecord> IncludedRecords { get; }

    /// <summary>
    /// Creates a new ContextSummary.
    /// </summary>
    public ContextSummary(Guid rootId, string summaryText, int tokenCount, IReadOnlyList<GraphRecord> includedRecords)
    {
        RootId = rootId;
        SummaryText = summaryText;
        TokenCount = tokenCount;
        IncludedRecords = includedRecords;
    }
}
