// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Holds the set of entitlement tags used for table-level and column-level access control.
/// Pass this to <see cref="SharcOpenOptions"/> to enforce entitlement-based filtering.
/// </summary>
/// <remarks>
/// Entitlement enforcement operates at the API level via <c>EntitlementEnforcer</c>,
/// which checks tags against agent scopes to grant or deny access to tables and columns.
/// Encryption is page-level (AES-256-GCM via <c>AesGcmPageTransform</c>), not row-level.
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