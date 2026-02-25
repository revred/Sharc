// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;

namespace Sharc.Archive;

/// <summary>
/// Parses JSON-lines input for conversation capture.
/// Each line is a JSON object representing a turn: { "role": "...", "content": "...", ... }
/// </summary>
public static class InputParser
{
    /// <summary>
    /// Parse a JSON-lines file into conversation and turn records.
    /// First line metadata (optional): { "conversation_id": "...", "title": "...", "agent_id": "..." }
    /// Subsequent lines: { "role": "user|assistant", "content": "...", "token_count": N }
    /// </summary>
    public static (ConversationRecord conversation, List<TurnRecord> turns) ParseJsonLines(
        string filePath, string? agentId = null, string? source = null)
    {
        var lines = File.ReadAllLines(filePath);
        return ParseJsonLines(lines, agentId, source);
    }

    /// <summary>Parse from string array (testable without file I/O).</summary>
    public static (ConversationRecord conversation, List<TurnRecord> turns) ParseJsonLines(
        string[] lines, string? agentId = null, string? source = null)
    {
        if (lines.Length == 0)
            throw new ArgumentException("Input is empty.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string conversationId = Guid.NewGuid().ToString("N")[..12];
        string? title = null;
        var turns = new List<TurnRecord>();

        int startLine = 0;

        // Try to parse first line as metadata
        var firstDoc = JsonDocument.Parse(lines[0]);
        if (firstDoc.RootElement.TryGetProperty("conversation_id", out var cidProp))
        {
            conversationId = cidProp.GetString() ?? conversationId;
            title = firstDoc.RootElement.TryGetProperty("title", out var tp) ? tp.GetString() : null;
            agentId = firstDoc.RootElement.TryGetProperty("agent_id", out var ap) ? ap.GetString() : agentId;
            source = firstDoc.RootElement.TryGetProperty("source", out var sp) ? sp.GetString() : source;
            startLine = 1;
        }

        for (int i = startLine; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var role = root.GetProperty("role").GetString() ?? "user";
            var content = root.GetProperty("content").GetString() ?? "";
            int? tokenCount = root.TryGetProperty("token_count", out var tc) ? tc.GetInt32() : null;

            turns.Add(new TurnRecord(conversationId, turns.Count, role, content, now + turns.Count, tokenCount, null));
        }

        long? endedAt = turns.Count > 0 ? now + turns.Count - 1 : null;
        var conversation = new ConversationRecord(conversationId, title, now, endedAt, agentId, source, null);

        return (conversation, turns);
    }
}
