// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// Parsed cross-arc reference URI. Immutable value type.
/// Format: <c>arc://{authority}/{path}#{table}/{rowid}</c>
/// <para>
/// Authority maps to an <see cref="IArcLocator"/> implementation:
/// <c>local</c> = filesystem, <c>https</c> = HTTP (future), <c>git</c> = git repository (future).
/// </para>
/// </summary>
public readonly struct ArcUri : IEquatable<ArcUri>
{
    /// <summary>Location authority: "local", "https", "git".</summary>
    public string Authority { get; }

    /// <summary>Path to the arc file (interpretation depends on authority).</summary>
    public string Path { get; }

    /// <summary>Optional table name from fragment (null if no fragment).</summary>
    public string? Table { get; }

    /// <summary>Optional rowid from fragment (-1 if not specified).</summary>
    public long RowId { get; }

    /// <summary>Whether this URI references a specific row in a table.</summary>
    public bool HasRowReference => Table != null;

    private ArcUri(string authority, string path, string? table, long rowId)
    {
        Authority = authority;
        Path = path;
        Table = table;
        RowId = rowId;
    }

    private const string Scheme = "arc://";

    /// <summary>
    /// Attempts to parse an arc URI string.
    /// Returns false for malformed input — never throws.
    /// </summary>
    public static bool TryParse(string? input, out ArcUri result)
    {
        result = default;
        if (string.IsNullOrEmpty(input))
            return false;

        if (!input.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<char> rest = input.AsSpan(Scheme.Length);
        if (rest.IsEmpty)
            return false;

        // Split authority from path at first '/'
        int slashIndex = rest.IndexOf('/');
        if (slashIndex <= 0)
            return false;

        string authority = rest.Slice(0, slashIndex).ToString().ToLowerInvariant();
        ReadOnlySpan<char> pathAndFragment = rest.Slice(slashIndex + 1);

        if (pathAndFragment.IsEmpty)
            return false;

        // Split path from fragment at '#'
        string? table = null;
        long rowId = -1;
        string path;

        int hashIndex = pathAndFragment.IndexOf('#');
        if (hashIndex >= 0)
        {
            path = pathAndFragment.Slice(0, hashIndex).ToString();
            ReadOnlySpan<char> fragment = pathAndFragment.Slice(hashIndex + 1);

            if (!fragment.IsEmpty)
            {
                int fragSlash = fragment.IndexOf('/');
                if (fragSlash >= 0)
                {
                    table = fragment.Slice(0, fragSlash).ToString();
                    if (long.TryParse(fragment.Slice(fragSlash + 1), out long parsed))
                        rowId = parsed;
                }
                else
                {
                    table = fragment.ToString();
                }
            }
        }
        else
        {
            path = pathAndFragment.ToString();
        }

        if (string.IsNullOrEmpty(path))
            return false;

        result = new ArcUri(authority, path, table, rowId);
        return true;
    }

    /// <summary>
    /// Parses an arc URI string. Throws <see cref="FormatException"/> on invalid input.
    /// </summary>
    public static ArcUri Parse(string input)
    {
        if (!TryParse(input, out var result))
            throw new FormatException($"Invalid arc URI: '{input}'");
        return result;
    }

    /// <summary>
    /// Creates a local arc URI from a file path.
    /// </summary>
    public static ArcUri FromLocalPath(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return new ArcUri("local", filePath, null, -1);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        string uri = $"{Scheme}{Authority}/{Path}";
        if (Table != null)
        {
            uri += $"#{Table}";
            if (RowId >= 0)
                uri += $"/{RowId}";
        }
        return uri;
    }

    /// <inheritdoc />
    public bool Equals(ArcUri other) =>
        string.Equals(Authority, other.Authority, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path, other.Path, StringComparison.Ordinal) &&
        string.Equals(Table, other.Table, StringComparison.Ordinal) &&
        RowId == other.RowId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ArcUri other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Authority?.ToLowerInvariant(), Path, Table, RowId);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ArcUri left, ArcUri right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ArcUri left, ArcUri right) => !left.Equals(right);
}
