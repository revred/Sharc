// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Holds the set of entitlement tags that the current caller is authorized to decrypt.
/// Pass this to <see cref="SharcOpenOptions"/> to enable row-level decryption for entitled rows.
/// Rows encrypted with tags not in this context are silently skipped during iteration.
/// </summary>
/// <remarks>
/// Row-level entitlement encryption allows a single database file to contain rows
/// encrypted with different keys, each derived from an entitlement tag. Only callers
/// holding the correct tags can decrypt and see those rows.
/// <para>
/// Entitlement tag examples: "tenant:acme", "role:admin", "team:engineering", "classification:pii".
/// </para>
/// </remarks>
public sealed class SharcEntitlementContext
{
    private readonly HashSet<string> _tags;

    /// <summary>
    /// Creates an entitlement context with the given tags.
    /// </summary>
    /// <param name="tags">Entitlement tags this caller is authorized for.</param>
    public SharcEntitlementContext(params string[] tags)
    {
        _tags = new HashSet<string>(tags, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the set of entitlement tags in this context.
    /// </summary>
    public IReadOnlyCollection<string> Tags => _tags;

    /// <summary>
    /// Returns true if this context includes the specified entitlement tag.
    /// </summary>
    /// <param name="tag">The entitlement tag to check.</param>
    public bool HasEntitlement(string tag) => _tags.Contains(tag);

    /// <summary>
    /// Returns true if this context has no entitlement tags.
    /// </summary>
    public bool IsEmpty => _tags.Count == 0;
}