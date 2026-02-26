// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Maintains commit-time synchronization between database row mutations
/// and attached in-memory HNSW indexes.
/// </summary>
internal static class HnswIndexAutoSyncRegistry
{
    private static readonly ConditionalWeakTable<SharcDatabase, DatabaseState> s_states = new();

    internal static void Register(SharcDatabase db, string tableName, string vectorColumn, HnswIndex index)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);
        ArgumentNullException.ThrowIfNull(index);

        var state = s_states.GetValue(db, static d => new DatabaseState(d));
        state.Register(tableName, vectorColumn, index);
    }

    private readonly record struct Registration(string TableName, string VectorColumn, HnswIndex Index);

    private sealed class DatabaseState : ITransactionCommitObserver
    {
        private readonly SharcDatabase _db;
        private readonly object _gate = new();
        private readonly List<Registration> _registrations = new();

        internal DatabaseState(SharcDatabase db)
        {
            _db = db;
            _db.RegisterTransactionCommitObserver(this);
        }

        internal void Register(string tableName, string vectorColumn, HnswIndex index)
        {
            lock (_gate)
            {
                for (int i = 0; i < _registrations.Count; i++)
                {
                    var existing = _registrations[i];
                    if (ReferenceEquals(existing.Index, index)
                        && existing.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)
                        && existing.VectorColumn.Equals(vectorColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                _registrations.Add(new Registration(tableName, vectorColumn, index));
            }
        }

        public void OnTransactionCommitted(SharcDatabase db, IReadOnlyList<TransactionRowMutation> mutations)
        {
            if (!ReferenceEquals(db, _db) || mutations.Count == 0)
                return;

            List<Registration> registrations;
            lock (_gate)
            {
                if (_registrations.Count == 0)
                    return;
                registrations = new List<Registration>(_registrations);
            }

            var touchedByTable = BuildTouchedRowMap(mutations);
            if (touchedByTable.Count == 0)
                return;

            var groupedIndexes = GroupByTableAndColumn(registrations, touchedByTable);
            if (groupedIndexes.Count == 0)
                return;

            var staleIndexes = new List<HnswIndex>();

            foreach (var tableGroup in groupedIndexes)
            {
                if (!touchedByTable.TryGetValue(tableGroup.Key, out var touchedRows) || touchedRows.Count == 0)
                    continue;

                foreach (var columnGroup in tableGroup.Value)
                {
                    using var reader = db.CreateReader(tableGroup.Key, columnGroup.Key);
                    foreach (long rowId in touchedRows)
                    {
                        if (reader.Seek(rowId))
                        {
                            try
                            {
                                ReadOnlySpan<float> vector = BlobVectorCodec.Decode(reader.GetBlobSpan(0));
                                for (int i = 0; i < columnGroup.Value.Count; i++)
                                    ApplyUpsert(columnGroup.Value[i], rowId, vector, staleIndexes);
                            }
                            catch
                            {
                                for (int i = 0; i < columnGroup.Value.Count; i++)
                                    ApplyDelete(columnGroup.Value[i], rowId, staleIndexes);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < columnGroup.Value.Count; i++)
                                ApplyDelete(columnGroup.Value[i], rowId, staleIndexes);
                        }
                    }
                }
            }

            if (staleIndexes.Count > 0)
                RemoveStaleRegistrations(staleIndexes);
        }

        private static Dictionary<string, HashSet<long>> BuildTouchedRowMap(
            IReadOnlyList<TransactionRowMutation> mutations)
        {
            var touched = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < mutations.Count; i++)
            {
                var mutation = mutations[i];
                if (!touched.TryGetValue(mutation.TableName, out var rows))
                {
                    rows = new HashSet<long>();
                    touched[mutation.TableName] = rows;
                }
                rows.Add(mutation.RowId);
            }
            return touched;
        }

        private static Dictionary<string, Dictionary<string, List<HnswIndex>>> GroupByTableAndColumn(
            List<Registration> registrations, Dictionary<string, HashSet<long>> touchedByTable)
        {
            var grouped = new Dictionary<string, Dictionary<string, List<HnswIndex>>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < registrations.Count; i++)
            {
                var registration = registrations[i];
                if (!touchedByTable.ContainsKey(registration.TableName))
                    continue;

                if (!grouped.TryGetValue(registration.TableName, out var columns))
                {
                    columns = new Dictionary<string, List<HnswIndex>>(StringComparer.OrdinalIgnoreCase);
                    grouped[registration.TableName] = columns;
                }

                if (!columns.TryGetValue(registration.VectorColumn, out var indexes))
                {
                    indexes = new List<HnswIndex>();
                    columns[registration.VectorColumn] = indexes;
                }

                indexes.Add(registration.Index);
            }

            return grouped;
        }

        private void RemoveStaleRegistrations(List<HnswIndex> staleIndexes)
        {
            lock (_gate)
            {
                for (int i = _registrations.Count - 1; i >= 0; i--)
                {
                    if (ContainsReference(staleIndexes, _registrations[i].Index))
                        _registrations.RemoveAt(i);
                }
            }
        }

        private static bool ContainsReference(List<HnswIndex> indexes, HnswIndex candidate)
        {
            for (int i = 0; i < indexes.Count; i++)
            {
                if (ReferenceEquals(indexes[i], candidate))
                    return true;
            }
            return false;
        }

        private static void ApplyUpsert(
            HnswIndex index, long rowId, ReadOnlySpan<float> vector, List<HnswIndex> staleIndexes)
        {
            try
            {
                index.Upsert(rowId, vector);
            }
            catch (ObjectDisposedException)
            {
                staleIndexes.Add(index);
            }
            catch (ArgumentException)
            {
                ApplyDelete(index, rowId, staleIndexes);
            }
            catch (InvalidOperationException)
            {
                ApplyDelete(index, rowId, staleIndexes);
            }
        }

        private static void ApplyDelete(HnswIndex index, long rowId, List<HnswIndex> staleIndexes)
        {
            try
            {
                index.Delete(rowId);
            }
            catch (ObjectDisposedException)
            {
                staleIndexes.Add(index);
            }
        }
    }
}
