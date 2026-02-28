// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Sharc.Core.Format;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Opportunistic schema cache for read-only OpenMemory calls.
/// Avoids repeated schema parsing when the same database bytes are reopened.
/// Cache is keyed by the underlying byte[] instance plus schema cookie + layout info.
/// </summary>
internal static class SchemaCache
{
    internal sealed class SchemaCacheBucket
    {
        private readonly object _gate = new();
        private readonly Dictionary<SchemaCacheKey, SharcSchema> _schemas = new();

        internal bool TryGet(SchemaCacheKey key, out SharcSchema schema)
        {
            lock (_gate)
                return _schemas.TryGetValue(key, out schema!);
        }

        internal void Store(SchemaCacheKey key, SharcSchema schema)
        {
            lock (_gate)
                _schemas[key] = schema;
        }
    }

    internal readonly struct SchemaCacheKey : IEquatable<SchemaCacheKey>
    {
        public readonly int Offset;
        public readonly int Length;
        public readonly int PageSize;
        public readonly uint SchemaCookie;

        public SchemaCacheKey(int offset, int length, int pageSize, uint schemaCookie)
        {
            Offset = offset;
            Length = length;
            PageSize = pageSize;
            SchemaCookie = schemaCookie;
        }

        public bool Equals(SchemaCacheKey other)
            => Offset == other.Offset && Length == other.Length
               && PageSize == other.PageSize && SchemaCookie == other.SchemaCookie;

        public override bool Equals(object? obj)
            => obj is SchemaCacheKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Offset, Length, PageSize, SchemaCookie);
    }

    private static readonly ConditionalWeakTable<byte[], SchemaCacheBucket> s_cache = new();

    internal static bool TryGet(ReadOnlyMemory<byte> data, DatabaseHeader header,
        out SharcSchema? schema, out SchemaCacheHandle? handle)
    {
        schema = null;
        handle = null;

        if (!MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) || segment.Array is null)
            return false;

        var key = new SchemaCacheKey(segment.Offset, segment.Count, header.PageSize, header.SchemaCookie);

        if (s_cache.TryGetValue(segment.Array, out var bucket) && bucket.TryGet(key, out var cached))
        {
            schema = cached;
            return true;
        }

        bucket ??= s_cache.GetOrCreateValue(segment.Array);
        handle = new SchemaCacheHandle(bucket, key);
        return false;
    }

    internal sealed class SchemaCacheHandle
    {
        private readonly SchemaCacheBucket _bucket;
        private readonly SchemaCacheKey _key;
        private int _stored;

        internal SchemaCacheHandle(SchemaCacheBucket bucket, SchemaCacheKey key)
        {
            _bucket = bucket;
            _key = key;
        }

        internal void Store(SharcSchema schema)
        {
            if (schema == null)
                return;

            if (Interlocked.Exchange(ref _stored, 1) != 0)
                return;

            _bucket.Store(_key, schema);
        }
    }
}
