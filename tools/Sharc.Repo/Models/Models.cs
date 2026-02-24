// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo;

public sealed record NoteRecord(
    string Content, string? Tag, string? Author,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record FileAnnotationRecord(
    string FilePath, string AnnotationType, string? Content,
    int? LineStart, int? LineEnd, string? Author,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record DecisionRecord(
    string DecisionId, string Title, string? Rationale,
    string Status, string? Author, long CreatedAt,
    IDictionary<string, object?>? Metadata);

public sealed record ContextEntry(
    string Key, string Value, string? Author,
    long CreatedAt, long UpdatedAt);

public sealed record ConversationTurnRecord(
    string SessionId, string Role, string Content,
    string? ToolName, long CreatedAt,
    IDictionary<string, object?>? Metadata);

public sealed record GitCommitRecord(
    string Sha, string AuthorName, string AuthorEmail,
    long AuthoredAt, string Message);

public sealed record GitFileChangeRecord(
    long CommitId, string Path, int LinesAdded, int LinesDeleted);
