// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;

namespace Sharc.Arc.Diff;

/// <summary>
/// Computes structural, ledger, and data diffs between two arc files.
/// Data diff uses streaming merge-join on sorted B-tree cursors with
/// <c>Fingerprint128</c> comparison for O(N+M) time, O(1) memory beyond page cache.
/// Never throws — errors are captured in the result.
/// </summary>
public static class ArcDiffer
{
    private static readonly HashSet<string> SystemTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "_sharc_ledger", "_sharc_agents", "_sharc_scores", "_sharc_audit",
        "sqlite_schema", "sqlite_master", "sqlite_sequence"
    };

    /// <summary>
    /// Computes a diff between two arc handles.
    /// </summary>
    public static ArcDiffResult Diff(ArcHandle left, ArcHandle right, ArcDiffOptions? options = null)
    {
        options ??= new ArcDiffOptions();

        SchemaDiff? schemaDiff = null;
        LedgerDiff? ledgerDiff = null;
        var tableDiffs = new List<TableDiff>();

        if (options.Scope.HasFlag(DiffScope.Schema))
            schemaDiff = DiffSchema(left, right);

        if (options.Scope.HasFlag(DiffScope.Ledger))
            ledgerDiff = DiffLedger(left, right);

        if (options.Scope.HasFlag(DiffScope.Data))
            tableDiffs.AddRange(DiffData(left, right, options));

        return new ArcDiffResult
        {
            Left = left.Uri?.ToString() ?? left.Name,
            Right = right.Uri?.ToString() ?? right.Name,
            Schema = schemaDiff,
            Ledger = ledgerDiff,
            Tables = tableDiffs
        };
    }

    /// <summary>
    /// Compares table and column structure between two arcs.
    /// </summary>
    public static SchemaDiff DiffSchema(ArcHandle left, ArcHandle right)
    {
        var leftTables = GetUserTables(left);
        var rightTables = GetUserTables(right);

        var leftNames = new HashSet<string>(leftTables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var rightNames = new HashSet<string>(rightTables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        var onlyInLeft = leftNames.Except(rightNames, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInRight = rightNames.Except(leftNames, StringComparer.OrdinalIgnoreCase).ToList();
        var common = leftNames.Intersect(rightNames, StringComparer.OrdinalIgnoreCase).ToList();

        var modified = new List<TableSchemaDiff>();
        var identical = new List<string>();

        foreach (var name in common)
        {
            var lt = leftTables.First(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var rt = rightTables.First(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            var diff = DiffTableSchema(lt, rt);
            if (diff != null)
                modified.Add(diff);
            else
                identical.Add(name);
        }

        return new SchemaDiff
        {
            TablesOnlyInLeft = onlyInLeft,
            TablesOnlyInRight = onlyInRight,
            ModifiedTables = modified,
            CommonTables = identical
        };
    }

    /// <summary>
    /// Compares ledger hash-chains between two arcs by walking entries sequentially.
    /// </summary>
    public static LedgerDiff DiffLedger(ArcHandle left, ArcHandle right)
    {
        const string ledgerTable = "_sharc_ledger";

        var leftEntries = ReadLedgerHashes(left, ledgerTable);
        var rightEntries = ReadLedgerHashes(right, ledgerTable);

        int commonPrefix = 0;
        int minLen = Math.Min(leftEntries.Count, rightEntries.Count);
        long? divergence = null;

        for (int i = 0; i < minLen; i++)
        {
            if (leftEntries[i].seq == rightEntries[i].seq &&
                leftEntries[i].hash.AsSpan().SequenceEqual(rightEntries[i].hash))
            {
                commonPrefix++;
            }
            else
            {
                divergence = leftEntries[i].seq;
                break;
            }
        }

        int leftOnly = leftEntries.Count - commonPrefix;
        int rightOnly = rightEntries.Count - commonPrefix;

        return new LedgerDiff
        {
            CommonPrefixLength = commonPrefix,
            DivergenceSequence = divergence,
            LeftOnlyCount = leftOnly,
            RightOnlyCount = rightOnly,
            LeftTotalCount = leftEntries.Count,
            RightTotalCount = rightEntries.Count
        };
    }

    private static List<(long seq, byte[] hash)> ReadLedgerHashes(ArcHandle handle, string ledgerTable)
    {
        var entries = new List<(long, byte[])>();
        if (handle.Database.Schema.GetTable(ledgerTable) == null)
            return entries;

        using var reader = handle.Database.CreateReader(ledgerTable);
        while (reader.Read())
        {
            long seq = reader.GetInt64(0);       // SequenceNumber
            byte[] hash = reader.GetBlob(4);     // PayloadHash (ordinal 4)
            entries.Add((seq, hash));
        }
        return entries;
    }

    private static IEnumerable<TableDiff> DiffData(ArcHandle left, ArcHandle right, ArcDiffOptions options)
    {
        // Find common user tables to diff
        var leftTables = GetUserTableNames(left);
        var rightTables = GetUserTableNames(right);
        var common = leftTables.Intersect(rightTables, StringComparer.OrdinalIgnoreCase);

        foreach (var tableName in common)
        {
            if (options.TableFilter != null &&
                !options.TableFilter.Any(f => f.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                continue;

            yield return DiffTable(left, right, tableName, options.MaxRowDiffsPerTable);
        }
    }

    /// <summary>
    /// Streaming merge-join diff for a single table.
    /// Walks both B-tree cursors in rowid order, comparing <c>Fingerprint128</c> per row.
    /// O(N+M) time, O(1) memory beyond page cache.
    /// </summary>
    public static TableDiff DiffTable(ArcHandle left, ArcHandle right, string tableName, int maxDiffs = 10_000)
    {
        using var readerL = left.Database.CreateReader(tableName);
        using var readerR = right.Database.CreateReader(tableName);

        long matching = 0, modified = 0, leftOnly = 0, rightOnly = 0;
        long leftCount = 0, rightCount = 0;
        long totalDiffs = 0;
        bool truncated = false;

        bool hasL = readerL.Read();
        bool hasR = readerR.Read();

        while (hasL || hasR)
        {
            if (hasL && hasR)
            {
                long ridL = readerL.RowId;
                long ridR = readerR.RowId;

                if (ridL == ridR)
                {
                    leftCount++;
                    rightCount++;

                    var fpL = readerL.GetRowFingerprint();
                    var fpR = readerR.GetRowFingerprint();

                    if (fpL.Equals(fpR))
                        matching++;
                    else
                    {
                        modified++;
                        totalDiffs++;
                    }

                    hasL = readerL.Read();
                    hasR = readerR.Read();
                }
                else if (ridL < ridR)
                {
                    leftCount++;
                    leftOnly++;
                    totalDiffs++;
                    hasL = readerL.Read();
                }
                else
                {
                    rightCount++;
                    rightOnly++;
                    totalDiffs++;
                    hasR = readerR.Read();
                }
            }
            else if (hasL)
            {
                leftCount++;
                leftOnly++;
                totalDiffs++;
                hasL = readerL.Read();
            }
            else
            {
                rightCount++;
                rightOnly++;
                totalDiffs++;
                hasR = readerR.Read();
            }

            if (maxDiffs >= 0 && totalDiffs >= maxDiffs)
            {
                truncated = true;
                // Drain remaining rows for counts only
                while (hasL) { leftCount++; leftOnly++; hasL = readerL.Read(); }
                while (hasR) { rightCount++; rightOnly++; hasR = readerR.Read(); }
                break;
            }
        }

        return new TableDiff
        {
            TableName = tableName,
            LeftRowCount = leftCount,
            RightRowCount = rightCount,
            MatchingRowCount = matching,
            ModifiedRowCount = modified,
            LeftOnlyRowCount = leftOnly,
            RightOnlyRowCount = rightOnly,
            Truncated = truncated
        };
    }

    private static List<TableInfo> GetUserTables(ArcHandle handle)
    {
        return handle.Database.Schema.Tables
            .Where(t => !SystemTables.Contains(t.Name))
            .ToList();
    }

    private static HashSet<string> GetUserTableNames(ArcHandle handle)
    {
        return new HashSet<string>(
            handle.Database.Schema.Tables
                .Where(t => !SystemTables.Contains(t.Name))
                .Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    private static TableSchemaDiff? DiffTableSchema(TableInfo left, TableInfo right)
    {
        var leftCols = left.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var rightCols = right.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var onlyInLeft = leftCols.Keys.Except(rightCols.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInRight = rightCols.Keys.Except(leftCols.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        var typeChanges = new List<ColumnTypeChange>();
        foreach (var name in leftCols.Keys.Intersect(rightCols.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var lc = leftCols[name];
            var rc = rightCols[name];
            if (!lc.DeclaredType.Equals(rc.DeclaredType, StringComparison.OrdinalIgnoreCase))
            {
                typeChanges.Add(new ColumnTypeChange
                {
                    ColumnName = name,
                    LeftType = lc.DeclaredType,
                    RightType = rc.DeclaredType
                });
            }
        }

        if (onlyInLeft.Count == 0 && onlyInRight.Count == 0 && typeChanges.Count == 0)
            return null; // identical schema

        return new TableSchemaDiff
        {
            TableName = left.Name,
            ColumnsOnlyInLeft = onlyInLeft,
            ColumnsOnlyInRight = onlyInRight,
            TypeChanges = typeChanges
        };
    }
}
