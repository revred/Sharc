// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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