// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// A mounted arc fragment within a <see cref="FusedArcContext"/>.
/// Carries provenance: every result can identify its source arc.
/// </summary>
public sealed class MountedArc
{
    /// <summary>The arc handle.</summary>
    public ArcHandle Handle { get; }

    /// <summary>Short name for provenance tagging (e.g., "conversations.arc").</summary>
    public string Alias { get; }

    internal MountedArc(ArcHandle handle, string alias)
    {
        Handle = handle;
        Alias = alias;
    }
}

/// <summary>
/// A query result row with source-arc provenance.
/// </summary>
public sealed class FusedRow
{
    /// <summary>The source arc this row came from.</summary>
    public string SourceArc { get; }

    /// <summary>The row id within the source arc.</summary>
    public long RowId { get; }

    /// <summary>The column values for this row.</summary>
    public IReadOnlyList<object?> Values { get; }

    internal FusedRow(string sourceArc, long rowId, IReadOnlyList<object?> values)
    {
        SourceArc = sourceArc;
        RowId = rowId;
        Values = values;
    }
}

/// <summary>
/// Multi-arc fusion engine. Mounts N <see cref="ArcHandle"/> instances and provides
/// unified query across all fragments with source-arc provenance.
/// <para>
/// <b>Design:</b> This class operates at the data layer (tables, rows, columns).
/// For graph-level fusion (Cypher, traversal, PageRank across arcs), callers
/// should iterate <see cref="Arcs"/> and create <c>SharcContextGraph</c> instances
/// per-arc from <c>Sharc.Graph</c> — keeping the dependency graph clean.
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// var fused = new FusedArcContext();
/// fused.Mount(ArcHandle.OpenLocal("conversations.arc"));
/// fused.Mount(ArcHandle.OpenLocal("codebase.arc"));
/// fused.Mount(ArcHandle.OpenLocal("knowledge.arc"));
/// 
/// // Query across all fragments
/// var rows = fused.Query("commits");
/// foreach (var row in rows)
///     Console.WriteLine($"{row.SourceArc}: {row.Values[0]}");
/// </code>
/// </para>
/// </summary>
public sealed class FusedArcContext : IDisposable
{
    private readonly List<MountedArc> _arcs = new();
    private bool _disposed;

    /// <summary>All currently mounted arcs.</summary>
    public IReadOnlyList<MountedArc> Arcs => _arcs;

    /// <summary>Number of mounted arcs.</summary>
    public int Count => _arcs.Count;

    /// <summary>
    /// Mounts an arc into the fused context.
    /// </summary>
    /// <param name="handle">The arc handle to mount.</param>
    /// <param name="alias">Optional display alias. Defaults to <see cref="ArcHandle.Name"/>.</param>
    /// <returns>The mounted arc descriptor.</returns>
    public MountedArc Mount(ArcHandle handle, string? alias = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handle);

        var mounted = new MountedArc(handle, alias ?? handle.Name);
        _arcs.Add(mounted);
        return mounted;
    }

    /// <summary>
    /// Unmounts an arc from the fused context. Does NOT dispose the handle.
    /// </summary>
    public bool Unmount(MountedArc arc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _arcs.Remove(arc);
    }

    /// <summary>
    /// Queries a table across all mounted arcs that contain it.
    /// Returns rows annotated with their source arc.
    /// </summary>
    /// <param name="tableName">The table name to query.</param>
    /// <param name="maxRows">Maximum rows per arc (0 = unlimited).</param>
    /// <returns>Fused rows with source-arc provenance.</returns>
    public List<FusedRow> Query(string tableName, int maxRows = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = new List<FusedRow>();

        foreach (var arc in _arcs)
        {
            Core.Schema.TableInfo? table;
            try { table = arc.Handle.Database.Schema.GetTable(tableName); }
            catch (KeyNotFoundException) { continue; }

            int colCount = table.Columns.Count;
            int rowCount = 0;

            using var reader = arc.Handle.Database.CreateReader(tableName);
            while (reader.Read())
            {
                if (maxRows > 0 && rowCount >= maxRows) break;

                var values = new object?[colCount];
                for (int c = 0; c < colCount; c++)
                {
                    if (reader.IsNull(c))
                        values[c] = null;
                    else
                    {
                        var declType = table.Columns[c].DeclaredType.ToUpperInvariant();
                        values[c] = declType switch
                        {
                            "INTEGER" or "INT" or "BIGINT" or "SMALLINT" or "TINYINT" => reader.GetInt64(c),
                            "REAL" or "FLOAT" or "DOUBLE" => reader.GetDouble(c),
                            "BLOB" => reader.GetBlob(c).ToArray(),
                            _ => reader.GetString(c)
                        };
                    }
                }

                results.Add(new FusedRow(arc.Alias, reader.RowId, values));
                rowCount++;
            }
        }

        return results;
    }

    /// <summary>
    /// Queries all mounted arcs and returns the tables each one contains.
    /// Useful for discovering what data is available across the fused context.
    /// </summary>
    public Dictionary<string, List<string>> DiscoverTables()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var arc in _arcs)
        {
            foreach (var table in arc.Handle.Database.Schema.Tables)
            {
                if (table.Name.StartsWith("_sharc_", StringComparison.OrdinalIgnoreCase) ||
                    table.Name.Equals("sqlite_master", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!result.TryGetValue(table.Name, out var sources))
                {
                    sources = new List<string>();
                    result[table.Name] = sources;
                }
                sources.Add(arc.Alias);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds all mounted arcs that contain the given table.
    /// </summary>
    public List<MountedArc> FindArcsWithTable(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var matches = new List<MountedArc>();
        foreach (var arc in _arcs)
        {
            try
            {
                arc.Handle.Database.Schema.GetTable(tableName);
                matches.Add(arc);
            }
            catch (KeyNotFoundException) { }
        }
        return matches;
    }

    /// <summary>
    /// Gets aggregate statistics across all mounted arcs.
    /// </summary>
    public (int ArcCount, int TotalTables) GetStats()
    {
        int tables = 0;

        foreach (var arc in _arcs)
            tables += arc.Handle.Database.Schema.Tables.Count;

        return (_arcs.Count, tables);
    }

    /// <summary>
    /// Verifies the integrity of all mounted arcs.
    /// Returns a list of arcs that failed verification.
    /// </summary>
    public List<(string Alias, string Error)> VerifyAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var failures = new List<(string, string)>();
        foreach (var arc in _arcs)
        {
            try
            {
                if (!arc.Handle.VerifyIntegrity())
                    failures.Add((arc.Alias, "Ledger integrity verification failed"));
            }
            catch (Exception ex)
            {
                failures.Add((arc.Alias, ex.Message));
            }
        }
        return failures;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var arc in _arcs)
            arc.Handle.Dispose();
        _arcs.Clear();
    }
}
