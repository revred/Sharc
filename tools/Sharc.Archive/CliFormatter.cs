// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;

namespace Sharc.Archive;

/// <summary>
/// Formats archive data for CLI output (table or JSON).
/// </summary>
public static class CliFormatter
{
    public static string FormatConversations(IReadOnlyList<ConversationRecord> conversations, string format = "table")
    {
        if (format == "json")
            return FormatJson(conversations.Select(c => new Dictionary<string, object?>
            {
                ["conversation_id"] = c.ConversationId,
                ["title"] = c.Title,
                ["started_at"] = c.StartedAt,
                ["ended_at"] = c.EndedAt,
                ["agent_id"] = c.AgentId,
                ["source"] = c.Source
            }));

        var sb = new StringBuilder();
        sb.AppendLine("| ID | Title | Started | Agent | Source |");
        sb.AppendLine("|----|-------|---------|-------|--------|");
        foreach (var c in conversations)
        {
            sb.AppendLine($"| {c.ConversationId} | {c.Title ?? "(none)"} | {c.StartedAt} | {c.AgentId ?? "-"} | {c.Source ?? "-"} |");
        }
        return sb.ToString();
    }

    public static string FormatTurns(IReadOnlyList<TurnRecord> turns, string format = "table")
    {
        if (format == "json")
            return FormatJson(turns.Select(t => new Dictionary<string, object?>
            {
                ["conversation_id"] = t.ConversationId,
                ["turn_index"] = t.TurnIndex,
                ["role"] = t.Role,
                ["content"] = t.Content.Length > 80 ? t.Content[..80] + "..." : t.Content,
                ["token_count"] = t.TokenCount
            }));

        var sb = new StringBuilder();
        sb.AppendLine("| Conv | # | Role | Content | Tokens |");
        sb.AppendLine("|------|---|------|---------|--------|");
        foreach (var t in turns)
        {
            var preview = t.Content.Length > 60 ? t.Content[..60] + "..." : t.Content;
            sb.AppendLine($"| {t.ConversationId} | {t.TurnIndex} | {t.Role} | {preview} | {t.TokenCount?.ToString() ?? "-"} |");
        }
        return sb.ToString();
    }

    public static string FormatAnnotations(IReadOnlyList<AnnotationRecord> annotations, string format = "table")
    {
        if (format == "json")
            return FormatJson(annotations.Select(a => new Dictionary<string, object?>
            {
                ["target_type"] = a.TargetType,
                ["target_id"] = a.TargetId,
                ["type"] = a.AnnotationType,
                ["verdict"] = a.Verdict,
                ["annotator"] = a.AnnotatorId
            }));

        var sb = new StringBuilder();
        sb.AppendLine("| Target | ID | Type | Verdict | Annotator |");
        sb.AppendLine("|--------|-----|------|---------|-----------|");
        foreach (var a in annotations)
        {
            sb.AppendLine($"| {a.TargetType} | {a.TargetId} | {a.AnnotationType} | {a.Verdict ?? "-"} | {a.AnnotatorId} |");
        }
        return sb.ToString();
    }

    public static string FormatDecisions(IReadOnlyList<DecisionRecord> decisions, string format = "table")
    {
        if (format == "json")
            return FormatJson(decisions.Select(d => new Dictionary<string, object?>
            {
                ["decision_id"] = d.DecisionId,
                ["title"] = d.Title,
                ["status"] = d.Status,
                ["rationale"] = d.Rationale
            }));

        var sb = new StringBuilder();
        sb.AppendLine("| Decision | Title | Status | Rationale |");
        sb.AppendLine("|----------|-------|--------|-----------|");
        foreach (var d in decisions)
        {
            sb.AppendLine($"| {d.DecisionId} | {d.Title} | {d.Status} | {d.Rationale ?? "-"} |");
        }
        return sb.ToString();
    }

    public static string FormatCheckpoints(IReadOnlyList<CheckpointRecord> checkpoints, string format = "table")
    {
        if (format == "json")
            return FormatJson(checkpoints.Select(cp => new Dictionary<string, object?>
            {
                ["checkpoint_id"] = cp.CheckpointId,
                ["label"] = cp.Label,
                ["conversations"] = cp.ConversationCount,
                ["turns"] = cp.TurnCount,
                ["annotations"] = cp.AnnotationCount
            }));

        var sb = new StringBuilder();
        sb.AppendLine("| Checkpoint | Label | Convs | Turns | Annots |");
        sb.AppendLine("|------------|-------|-------|-------|--------|");
        foreach (var cp in checkpoints)
        {
            sb.AppendLine($"| {cp.CheckpointId} | {cp.Label} | {cp.ConversationCount} | {cp.TurnCount} | {cp.AnnotationCount} |");
        }
        return sb.ToString();
    }

    private static string FormatJson(IEnumerable<Dictionary<string, object?>> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        bool first = true;
        foreach (var item in items)
        {
            if (!first) sb.AppendLine(",");
            first = false;
            sb.Append("  {");
            bool firstProp = true;
            foreach (var kv in item)
            {
                if (!firstProp) sb.Append(", ");
                firstProp = false;
                sb.Append($"\"{kv.Key}\": ");
                sb.Append(kv.Value == null ? "null"
                    : kv.Value is string s ? $"\"{EscapeJson(s)}\""
                    : kv.Value.ToString());
            }
            sb.Append("}");
        }
        sb.AppendLine();
        sb.AppendLine("]");
        return sb.ToString();
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
