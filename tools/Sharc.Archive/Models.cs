// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Archive;

public sealed record ConversationRecord(
    string ConversationId, string? Title, long StartedAt,
    long? EndedAt, string? AgentId, string? Source,
    IDictionary<string, object?>? Metadata);

public sealed record TurnRecord(
    string ConversationId, int TurnIndex, string Role,
    string Content, long CreatedAt, int? TokenCount,
    IDictionary<string, object?>? Metadata);

public sealed record AnnotationRecord(
    string TargetType, long TargetId, string AnnotationType,
    string? Verdict, string? Content, string AnnotatorId,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record FileAnnotationRecord(
    long TurnId, string FilePath, string AnnotationType,
    string? Content, int? LineStart, int? LineEnd,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record DecisionRecord(
    string ConversationId, long? TurnId, string DecisionId,
    string Title, string? Rationale, string Status,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record CheckpointRecord(
    string CheckpointId, string Label, long CreatedAt,
    int ConversationCount, int TurnCount, int AnnotationCount,
    long LedgerSequence, IDictionary<string, object?>? Metadata);
