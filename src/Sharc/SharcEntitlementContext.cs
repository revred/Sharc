/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
