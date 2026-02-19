// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Combines multiple <see cref="IEntitlementProvider"/> instances into a single multi-dimensional scope.
/// Scopes are joined with '|' (e.g., "tenant:acme|role:admin").
/// Providers that return null are skipped. If all return null, the combined scope is null.
/// </summary>
public sealed class CompositeEntitlementProvider : IEntitlementProvider
{
    private readonly IEntitlementProvider[] _providers;

    /// <summary>
    /// Creates a composite provider from two or more inner providers.
    /// </summary>
    /// <param name="providers">The inner providers to combine.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="providers"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="providers"/> is empty.</exception>
    public CompositeEntitlementProvider(params IEntitlementProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Length == 0)
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        _providers = providers;
    }

    /// <inheritdoc/>
    public string? GetScope()
    {
        Span<int> indices = stackalloc int[_providers.Length];
        int count = 0;
        int totalLength = 0;

        for (int i = 0; i < _providers.Length; i++)
        {
            var scope = _providers[i].GetScope();
            if (scope is not null)
            {
                indices[count++] = i;
                totalLength += scope.Length;
            }
        }

        if (count == 0)
            return null;

        if (count == 1)
            return _providers[indices[0]].GetScope();

        // Build combined scope with '|' separator
        totalLength += count - 1; // separators
        return string.Create(totalLength, (Providers: _providers, Indices: indices[..count].ToArray()), static (span, state) =>
        {
            int pos = 0;
            for (int i = 0; i < state.Indices.Length; i++)
            {
                if (i > 0)
                    span[pos++] = '|';

                var scope = state.Providers[state.Indices[i]].GetScope()!;
                scope.AsSpan().CopyTo(span[pos..]);
                pos += scope.Length;
            }
        });
    }
}
