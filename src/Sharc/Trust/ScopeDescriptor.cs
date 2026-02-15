// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Trust;

/// <summary>
/// Parses and evaluates scope strings for entitlement enforcement.
/// Format: comma-separated entries of "table.*" or "table.column" or "prefix*.*".
/// Use "*" for unrestricted access.
/// </summary>
internal readonly struct ScopeDescriptor
{
    private readonly ScopeEntry[]? _entries;

    /// <summary>True if the scope grants unrestricted access.</summary>
    internal bool IsUnrestricted { get; }

    private ScopeDescriptor(bool unrestricted, ScopeEntry[]? entries)
    {
        IsUnrestricted = unrestricted;
        _entries = entries;
    }

    /// <summary>
    /// Parses a scope string into a <see cref="ScopeDescriptor"/>.
    /// </summary>
    internal static ScopeDescriptor Parse(ReadOnlySpan<char> scope)
    {
        scope = scope.Trim();
        if (scope.IsEmpty)
            return new ScopeDescriptor(false, null);

        if (scope is "*")
            return new ScopeDescriptor(true, null);

        var entries = new List<ScopeEntry>();
        foreach (var segment in scope.ToString().Split(','))
        {
            var entry = segment.Trim();
            if (string.IsNullOrEmpty(entry)) continue;

            int dotIndex = entry.IndexOf('.');
            if (dotIndex < 0)
            {
                // Table-level only (no dot) — treat as table.*
                entries.Add(new ScopeEntry(entry, null, false));
                continue;
            }

            string tablePart = entry[..dotIndex];
            string columnPart = entry[(dotIndex + 1)..];

            bool isWildcardTable = tablePart.EndsWith('*');
            string tablePrefix = isWildcardTable ? tablePart[..^1] : tablePart;
            bool allColumns = columnPart == "*";

            entries.Add(new ScopeEntry(tablePrefix, allColumns ? null : columnPart, isWildcardTable));
        }

        return new ScopeDescriptor(false, entries.ToArray());
    }

    /// <summary>
    /// Returns true if the scope permits reading the given table.
    /// </summary>
    internal bool CanReadTable(string tableName)
    {
        if (IsUnrestricted) return true;
        if (_entries == null) return false;

        foreach (var entry in _entries)
        {
            if (entry.MatchesTable(tableName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the scope permits reading the given column in the given table.
    /// </summary>
    internal bool CanReadColumn(string tableName, string columnName)
    {
        if (IsUnrestricted) return true;
        if (_entries == null) return false;

        foreach (var entry in _entries)
        {
            if (entry.MatchesTable(tableName) && entry.MatchesColumn(columnName))
                return true;
        }

        return false;
    }

    private readonly struct ScopeEntry
    {
        private readonly string _tableOrPrefix;
        private readonly string? _column; // null = all columns
        private readonly bool _isWildcardTable;

        internal ScopeEntry(string tableOrPrefix, string? column, bool isWildcardTable)
        {
            _tableOrPrefix = tableOrPrefix;
            _column = column;
            _isWildcardTable = isWildcardTable;
        }

        internal bool MatchesTable(string tableName)
        {
            if (_isWildcardTable)
                return tableName.StartsWith(_tableOrPrefix, StringComparison.Ordinal);
            return string.Equals(_tableOrPrefix, tableName, StringComparison.Ordinal);
        }

        internal bool MatchesColumn(string columnName)
        {
            // null = all columns allowed (table.*)
            if (_column == null) return true;
            return string.Equals(_column, columnName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
