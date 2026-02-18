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

    /// <summary>True if the scope grants schema modification rights.</summary>
    internal bool IsSchemaAdmin { get; }

    private ScopeDescriptor(bool unrestricted, bool schemaAdmin, ScopeEntry[]? entries)
    {
        IsUnrestricted = unrestricted;
        IsSchemaAdmin = schemaAdmin;
        _entries = entries;
    }

    /// <summary>
    /// Parses a scope string into a <see cref="ScopeDescriptor"/>.
    /// </summary>
    internal static ScopeDescriptor Parse(ReadOnlySpan<char> scope)
    {
        scope = scope.Trim();
        if (scope.IsEmpty)
            return new ScopeDescriptor(false, false, null);

        if (scope.SequenceEqual("*".AsSpan()))
            return new ScopeDescriptor(true, true, null);

        bool schemaAdmin = false;
        var entries = new List<ScopeEntry>();
        
        // Manual split loop
        int start = 0;
        for (int i = 0; i <= scope.Length; i++)
        {
            if (i == scope.Length || scope[i] == ',')
            {
                var segment = scope.Slice(start, i - start).Trim();
                start = i + 1;
                
                if (segment.IsEmpty) continue;

                if (segment.Equals(".schema".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    schemaAdmin = true;
                    continue;
                }

                int dotIndex = segment.IndexOf('.');
                if (dotIndex < 0)
                {
                    // Table-level only (no dot) — treat as table.*
                    entries.Add(new ScopeEntry(segment.ToString(), null, false));
                    continue;
                }

                var tablePart = segment[..dotIndex];
                var columnPart = segment[(dotIndex + 1)..];

                bool isWildcardTable = tablePart.EndsWith('*');
                string tablePrefix = isWildcardTable ? tablePart[..^1].ToString() : tablePart.ToString();
                bool allColumns = columnPart.SequenceEqual("*".AsSpan());

                string? colName = allColumns ? null : columnPart.ToString();
                entries.Add(new ScopeEntry(tablePrefix, colName, isWildcardTable));
            }
        }

        return new ScopeDescriptor(false, schemaAdmin, entries.ToArray());
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
