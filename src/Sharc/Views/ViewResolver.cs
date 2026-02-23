// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Intent;
using Sharc.Query.Sharq;

namespace Sharc.Views;

/// <summary>
/// Manages programmatic view registration and SQL-level view resolution.
/// Holds the registered view dictionary and generation counter, and provides
/// methods for resolving view references in query plans into Cote (CTE) SQL.
/// Not thread-safe — relies on the parent <see cref="SharcDatabase"/> single-thread contract.
/// </summary>
internal sealed class ViewResolver
{
    private readonly SharcDatabase _db;
    private Dictionary<string, SharcView>? _registeredViews;
    private int _registeredViewGeneration;

    internal ViewResolver(SharcDatabase db)
    {
        _db = db;
    }

    /// <summary>Whether any views are currently registered.</summary>
    internal bool HasRegisteredViews => _registeredViews is { Count: > 0 };

    /// <summary>
    /// The current generation counter. Returns -1 when no views are registered,
    /// which acts as a sentinel to invalidate cached plans that were resolved with views.
    /// </summary>
    internal int EffectiveGeneration => HasRegisteredViews ? _registeredViewGeneration : -1;

    // ─── Public registration API ────────────────────────────────────

    internal void RegisterView(SharcView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrWhiteSpace(view.Name))
            throw new ArgumentException("View name must not be empty or whitespace.", nameof(view));
        _registeredViews ??= new Dictionary<string, SharcView>(StringComparer.OrdinalIgnoreCase);
        _registeredViews[view.Name] = view;
        _registeredViewGeneration++;
    }

    internal IReadOnlyCollection<string> ListRegisteredViews()
    {
        if (_registeredViews is not { Count: > 0 })
            return Array.Empty<string>();
        return _registeredViews.Keys;
    }

    internal bool UnregisterView(string viewName)
    {
        if (_registeredViews != null && _registeredViews.Remove(viewName))
        {
            _registeredViewGeneration++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves a view by name without opening a cursor.
    /// Checks registered views first, then falls back to auto-promotable SQLite schema views.
    /// Returns null if the view doesn't exist or isn't promotable.
    /// </summary>
    internal SharcView? TryResolveView(string viewName)
    {
        if (_registeredViews != null &&
            _registeredViews.TryGetValue(viewName, out var registeredView))
        {
            return registeredView;
        }

        var schema = _db.GetSchemaInternal();
        var viewInfo = schema.GetView(viewName);
        if (viewInfo != null)
            return ViewPromoter.TryPromote(viewInfo, schema);

        return null;
    }

    internal IViewCursor OpenView(string viewName)
    {
        // Check registered programmatic views first
        if (_registeredViews != null &&
            _registeredViews.TryGetValue(viewName, out var registeredView))
        {
            return registeredView.Open(_db);
        }

        // Fall back to SQLite schema views
        var schema = _db.GetSchemaInternal();
        var viewInfo = schema.GetView(viewName)
            ?? throw new KeyNotFoundException($"View '{viewName}' not found in schema.");

        var nativeView = ViewPromoter.TryPromote(viewInfo, schema)
            ?? throw new KeyNotFoundException(
                $"View '{viewName}' is too complex for native execution (has JOIN, WHERE, or multiple tables). " +
                "Use db.Query() instead.");

        return nativeView.Open(_db);
    }

    // ─── View resolution for query plans ────────────────────────────

    /// <summary>
    /// Resolves view references in a query plan by injecting Cote (CTE) definitions.
    /// Iteratively discovers view table references, builds synthetic SQL for registered
    /// views, and falls back to SQLite schema views. Emits Cotes in topological order.
    /// </summary>
    internal QueryPlan ResolveViews(QueryPlan plan)
    {
        var resolvedViews = new Dictionary<string, QueryPlan>(StringComparer.OrdinalIgnoreCase);

        if (plan.Cotes != null)
        {
            foreach (var c in plan.Cotes)
                resolvedViews[c.Name] = c.Query;
        }

        bool changed = true;
        int iterations = 0;
        while (changed && iterations++ < 10)
        {
            changed = false;
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectTableRefs(plan, references);
            foreach (var viewPlan in resolvedViews.Values)
                CollectTableRefs(viewPlan, references);

            foreach (var table in references)
            {
                if (resolvedViews.ContainsKey(table)) continue;

                // Check registered programmatic views first
                if (_registeredViews != null &&
                    _registeredViews.TryGetValue(table, out var regView))
                {
                    string syntheticSql = BuildViewCoteSql(regView);
                    var viewPlan = IntentCompiler.CompilePlan(syntheticSql);
                    resolvedViews[table] = viewPlan;
                    changed = true;
                    continue;
                }

                // Fall back to SQLite schema views
                var view = _db.GetSchemaInternal().GetView(table);
                if (view != null)
                {
                    string viewSql = ViewSqlScanner.ExtractQuery(view.Sql);
                    var viewPlan = IntentCompiler.CompilePlan(viewSql);
                    resolvedViews[table] = viewPlan;
                    changed = true;
                }
            }
        }

        if (iterations >= 10 && changed)
            throw new InvalidOperationException("Recursive view definition depth exceeded limit (10).");

        if (resolvedViews.Count > (plan.Cotes?.Count ?? 0))
        {
            // Topological sort: emit Cotes in dependency order so that when CoteExecutor
            // materializes them sequentially, each Cote's referenced tables are already
            // available. Uses DFS with a "processing" set for cycle detection.
            var sortedCotes = new List<CoteIntent>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string name, QueryPlan p)
            {
                if (visited.Contains(name)) return;
                if (processing.Contains(name))
                    throw new InvalidOperationException($"Cyclic view dependency detected: {name}");

                processing.Add(name);

                var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectTableRefs(p, deps);

                foreach (var dep in deps)
                {
                    if (resolvedViews.TryGetValue(dep, out var depPlan))
                    {
                        Visit(dep, depPlan);
                    }
                }

                processing.Remove(name);
                visited.Add(name);
                sortedCotes.Add(new CoteIntent { Name = name, Query = p });
            }

            foreach (var kvp in resolvedViews)
            {
                Visit(kvp.Key, kvp.Value);
            }

            return new QueryPlan
            {
                Simple = plan.Simple,
                Compound = plan.Compound,
                Cotes = sortedCotes
            };
        }
        return plan;
    }

    /// <summary>
    /// Pre-materializes registered views that have filters (Func predicates)
    /// which cannot be expressed as SQL. Opens the view cursor, collects rows
    /// into QueryValue arrays, and returns a dictionary of pre-materialized results.
    /// </summary>
    internal CoteMap? PreMaterializeFilteredViews(QueryPlan plan)
    {
        if (_registeredViews == null || _registeredViews.Count == 0)
            return null;

        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTableRefs(plan, references);
        if (plan.Cotes != null)
        {
            foreach (var cote in plan.Cotes)
                CollectTableRefs(cote.Query, references);
        }

        CoteMap? result = null;

        foreach (var tableName in references)
        {
            if (!_registeredViews.TryGetValue(tableName, out var regView))
                continue;

            if (!ViewChainHasFilter(regView))
                continue;

            // Materialize the view cursor. The using declaration ensures disposal
            // even if an exception occurs during row materialization.
            using var cursor = regView.Open(_db);
            int fieldCount = cursor.FieldCount;
            var columnNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                columnNames[i] = cursor.GetColumnName(i);

            var rows = new RowSet();
            while (cursor.MoveNext())
            {
                var row = new QueryValue[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                {
                    if (cursor.IsNull(i))
                    {
                        row[i] = QueryValue.Null;
                        continue;
                    }
                    row[i] = cursor.GetColumnType(i) switch
                    {
                        SharcColumnType.Integral => QueryValue.FromInt64(cursor.GetInt64(i)),
                        SharcColumnType.Real => QueryValue.FromDouble(cursor.GetDouble(i)),
                        SharcColumnType.Text => QueryValue.FromString(cursor.GetString(i)),
                        SharcColumnType.Blob => QueryValue.FromBlob(cursor.GetBlob(i)),
                        _ => QueryValue.Null,
                    };
                }
                rows.Add(row);
            }

            result ??= new CoteMap(StringComparer.OrdinalIgnoreCase);
            result[tableName] = new MaterializedResultSet(rows, columnNames);
        }

        return result;
    }

    // ─── Static helpers ─────────────────────────────────────────────

    /// <summary>
    /// Builds synthetic SQL for a registered view to use as a Cote placeholder.
    /// Walks the view chain to find the root table and uses the outermost projection.
    /// </summary>
    internal static string BuildViewCoteSql(SharcView view)
    {
        var current = view;
        while (current.SourceView != null)
            current = current.SourceView;

        string rootTable = current.SourceTable!;

        if (view.ProjectedColumnNames is { Count: > 0 })
        {
            var cols = string.Join(", ", view.ProjectedColumnNames.Select(c => $"[{c}]"));
            return $"SELECT {cols} FROM [{rootTable}]";
        }

        return $"SELECT * FROM [{rootTable}]";
    }

    /// <summary>
    /// Returns true if the view (or any ancestor in its chain) has a Func filter.
    /// </summary>
    internal static bool ViewChainHasFilter(SharcView view)
    {
        var current = view;
        while (current != null)
        {
            if (current.Filter != null) return true;
            current = current.SourceView;
        }
        return false;
    }

    /// <summary>
    /// Recursively walks a <see cref="QueryPlan"/> tree and collects every
    /// table name referenced (including JOIN targets).
    /// </summary>
    internal static void CollectTableRefs(QueryPlan plan, HashSet<string> references)
    {
        if (plan.Simple != null)
        {
            references.Add(plan.Simple.TableName);
            if (plan.Simple.Joins != null)
                foreach (var j in plan.Simple.Joins) references.Add(j.TableName);
        }
        if (plan.Compound != null)
        {
            CollectTableRefs(plan.Compound, references);
        }
    }

    /// <summary>
    /// Recursively walks a compound query plan and collects all table names.
    /// </summary>
    internal static void CollectTableRefs(CompoundQueryPlan compound, HashSet<string> references)
    {
        if (compound.Left != null) references.Add(compound.Left.TableName);
        if (compound.RightSimple != null) references.Add(compound.RightSimple.TableName);
        if (compound.RightCompound != null) CollectTableRefs(compound.RightCompound, references);
    }
}
