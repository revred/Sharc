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

// ── Knowledge Graph records ──────────────────────────────────────

public sealed record FeatureRecord(
    string Name, string? Description, string Layer,
    string Status, long CreatedAt,
    IDictionary<string, object?>? Metadata);

public sealed record FeatureEdgeRecord(
    string FeatureName, string TargetPath, string TargetKind,
    string? Role, bool AutoDetected, long CreatedAt,
    IDictionary<string, object?>? Metadata);

public sealed record FilePurposeRecord(
    string Path, string Purpose, string Project,
    string? Namespace, string? Layer, bool AutoDetected,
    long CreatedAt, IDictionary<string, object?>? Metadata);

public sealed record FileDepRecord(
    string SourcePath, string TargetPath, string DepKind,
    bool AutoDetected, long CreatedAt);
