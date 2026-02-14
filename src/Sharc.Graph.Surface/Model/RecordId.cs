// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Globalization;

namespace Sharc.Graph.Model;

/// <summary>
/// A typed record identifier supporting both "table:id" format (SurrealDB-style)
/// and integer key addressing (Maker.AI-style). Immutable value object.
/// </summary>
public record struct RecordId
{
    private string? _id;
    private string? _fullId;
    private readonly bool _hasKey;

    /// <summary>The table or type name part of the ID.</summary>
    public string Table { get; }
    
    /// <summary>The unique identifier part of the ID (string GUID or user ID).</summary>
    public string Id => _id ??= "pk:" + Key.Value;
    
    /// <summary>The optimized integer key for this record, if available.</summary>
    public NodeKey Key { get; }
    
    /// <summary>The full "table:id" string representation.</summary>
    public string FullId => _fullId ??= $"{Table}:{Id}";
    
    /// <summary>True if this ID has an associated integer key (including zero).</summary>
    public bool HasIntegerKey => _hasKey;

    /// <summary>
    /// Creates a RecordId from a table and ID string.
    /// </summary>
    /// <param name="table">Table name.</param>
    /// <param name="id">String ID.</param>
    /// <param name="key">Optional integer key.</param>
    public RecordId(string table, string? id, NodeKey? key = null)
    {
        if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("Table cannot be empty", nameof(table));
        
        Table = table;
        _id = id;
        Key = key ?? default;
        _hasKey = key.HasValue;
        _fullId = null;
    }

    /// <summary>
    /// Creates a RecordId from an integer TypeID and GUID (Adapter mode).
    /// </summary>
    /// <param name="typeId">The integer type ID (becomes Table).</param>
    /// <param name="idString">The string ID.</param>
    /// <param name="key">The integer key (BarID).</param>
    public RecordId(int typeId, string? idString, NodeKey key)
    {
        Table = typeId.ToString(CultureInfo.InvariantCulture);
        _id = idString;
        Key = key;
        _hasKey = true;
        _fullId = null;
    }

    /// <summary>
    /// Parses a "table:id" string into a RecordId.
    /// </summary>
    /// <param name="fullId">The full ID string.</param>
    /// <returns>A new RecordId.</returns>
    /// <exception cref="FormatException">If the format is invalid.</exception>
    public static RecordId Parse(string fullId)
    {
        if (string.IsNullOrWhiteSpace(fullId)) throw new FormatException("RecordId cannot be empty");

        var colonIndex = fullId.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= fullId.Length - 1)
            throw new FormatException($"Invalid RecordId: '{fullId}'. Expected 'table:id'.");
            
        return new RecordId(fullId[..colonIndex], fullId[(colonIndex + 1)..]);
    }

    /// <summary>
    /// Tries to parse a "table:id" string.
    /// </summary>
    /// <param name="value">The input string.</param>
    /// <param name="result">The resulting RecordId if successful.</param>
    /// <returns>True if parsed successfully.</returns>
    public static bool TryParse(string? value, out RecordId result)
    {
        result = default;
        if (string.IsNullOrEmpty(value)) return false;
        
        var colonIndex = value.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= value.Length - 1) return false;
        
        result = new RecordId(value[..colonIndex], value[(colonIndex + 1)..]);
        return true;
    }

    /// <summary>
    /// Checks if a string span looks like a valid record link (contains exactly one colon, no spaces/slashes).
    /// </summary>
    /// <param name="value">The span to check.</param>
    /// <returns>True if it looks like a link.</returns>
    public static bool IsRecordLink(ReadOnlySpan<char> value)
    {
        var colonIndex = value.IndexOf(':');
        // Must contain colon, not start with it, not end with it
        if (colonIndex <= 0 || colonIndex >= value.Length - 1) return false;
        
        // Should not contain slashes (URLs) or spaces
        if (value.Contains('/') || value.Contains(' ')) return false;
        
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => FullId;
    
    /// <summary>
    /// Implicitly converts a RecordId to its string representation.
    /// </summary>
    public static implicit operator string(RecordId id) => id.FullId;
}