// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;

namespace Sharc.Query;

/// <summary>
/// Computes a deterministic fingerprint for parameter dictionaries.
/// Keys are sorted ordinally so insertion order does not affect the result.
/// </summary>
internal static class ParameterKeyHasher
{
    private static readonly OrdinalKeyComparer s_keyComparer = new();

    internal static long Compute(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null or { Count: 0 })
            return 0;

        if (parameters.Count == 1)
        {
            foreach (var pair in parameters)
            {
                var single = new HashCode();
                single.Add(pair.Key, StringComparer.Ordinal);
                single.Add(pair.Value);
                return single.ToHashCode();
            }
        }

        int count = parameters.Count;
        var rented = ArrayPool<KeyValuePair<string, object>>.Shared.Rent(count);
        try
        {
            int i = 0;
            foreach (var pair in parameters)
                rented[i++] = pair;

            Array.Sort(rented, 0, count, s_keyComparer);

            var hc = new HashCode();
            for (int idx = 0; idx < count; idx++)
            {
                ref readonly var pair = ref rented[idx];
                hc.Add(pair.Key, StringComparer.Ordinal);
                hc.Add(pair.Value);
            }

            return hc.ToHashCode();
        }
        finally
        {
            Array.Clear(rented, 0, count);
            ArrayPool<KeyValuePair<string, object>>.Shared.Return(rented, clearArray: false);
        }
    }

    private sealed class OrdinalKeyComparer : IComparer<KeyValuePair<string, object>>
    {
        public int Compare(KeyValuePair<string, object> x, KeyValuePair<string, object> y)
            => StringComparer.Ordinal.Compare(x.Key, y.Key);
    }
}
