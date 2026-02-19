# Query Pipeline Overview

## Execution Flow

```
User calls db.Query(sql)
        │
        ▼
┌───────────────────┐
│  SQL Parser        │ Sharq.SqlTokenizer → QueryPlan
│  (Sharq/)          │ Parses SELECT, WHERE, JOIN, ORDER BY, LIMIT,
│                    │ WITH (Cotes), UNION/INTERSECT/EXCEPT
└────────┬──────────┘
         │
         ▼
┌───────────────────┐
│  View Resolution   │ SharcDatabase.ResolveViews()
│  (SharcDatabase)   │ Replaces registered view names with Cote SQL
│                    │ (SELECT * FROM <source_table>), pre-materializes
│                    │ views with programmatic filters
└────────┬──────────┘
         │
         ▼
┌───────────────────┐
│  Query Plan Cache  │ QueryPlanCache (intent-keyed)
│                    │ Returns cached plan if SQL matches
└────────┬──────────┘
         │
         ▼
   ┌─────┴─────┐
   │ Has Cotes? │
   └─┬───────┬─┘
    Yes      No
     │        │
     ▼        ▼
┌──────────┐  ┌──────────────────┐
│ Compound │  │ Simple Executor   │
│ Query    │  │                   │
│ Executor │  │ CreateReaderFrom- │
│          │  │ Intent → reader   │
│ Lazy     │  │ + PostProcessor   │
│ Cote     │  └────────┬─────────┘
│ resolve  │           │
│ or full  │           ▼
│ material-│  ┌──────────────────┐
│ ize      │  │  Has JOINs?      │
└────┬─────┘  └──┬────────────┬──┘
     │          Yes           No
     │           │             │
     ▼           ▼             ▼
  (merge)  ┌──────────┐  ┌──────────────┐
     │     │ Join      │  │ Table Cursor  │
     │     │ Executor  │  │ + Filters     │
     │     │           │  │ (FilterStar/  │
     │     │ Hash Join │  │  PredicateNode│
     │     │ + push-   │  │  or JIT)      │
     │     │ down      │  └──────┬───────┘
     │     └─────┬────┘         │
     │           │              │
     ▼           ▼              ▼
┌────────────────────────────────────┐
│  QueryPostProcessor                │
│  Aggregates → DISTINCT → ORDER BY  │
│  → LIMIT/OFFSET → SharcDataReader  │
└────────────────────────────────────┘
```

## Key Components

### SharcDatabase.QueryCore()
Entry point. Resolves registered views, checks the query plan cache, and dispatches
to CompoundQueryExecutor (for CTEs/UNION) or the simple path (CreateReaderFromIntent).

### CompoundQueryExecutor
Handles CTEs (Cotes) and set operations. **Lazy Cote resolution** inlines simple
CTE references as direct table intents so streaming paths remain available.
Falls back to full materialization only for filtered views or complex dependencies.

### CoteExecutor
Materializes CTE results eagerly in dependency order. Each CTE's query executes
with all previously materialized CTEs available (forward reference support).

### JoinExecutor
Implements Hash Join. Builds a hash table from the smaller (build) side, then
probes with the larger (probe) side. `SplitFilters` pushes predicates down to
individual tables before the join to minimize rows entering the hash table.

### Filter Pipeline (Two Tiers)
- **Tier 1 (PredicateNode):** Interprets filter predicates directly on raw record
  bytes. Zero allocation. Supports cross-type comparisons (int vs double).
- **Tier 2 (JIT/Baked):** Compiles predicates into Expression Trees for repeated
  evaluation. Same semantics, higher throughput on large scans.

### QueryPostProcessor
Applies post-scan operations: aggregation (GROUP BY), DISTINCT, ORDER BY,
LIMIT/OFFSET. Operates on materialized `QueryValue[]` rows.

## View Resolution Pipeline

```
RegisterView(SharcView)
        │
        ▼
_registeredViews[name] = view
        │
        ▼
Query("SELECT * FROM view_name")
        │
        ▼
ResolveViews() detects view_name in registered views
        │
   ┌────┴────┐
   │ Has     │
   │ filter? │
   └─┬─────┬─┘
    Yes    No
     │      │
     ▼      ▼
 PreMaterialize   Inject as Cote:
 rows into       WITH view_name AS
 CoteMap         (SELECT cols FROM source)
     │              │
     └──────┬───────┘
            ▼
     Normal query execution
     (Cote-aware paths)
```

## Type Aliases

- `RowSet` = `List<QueryValue[]>` — a set of materialized rows
- `CoteMap` = `Dictionary<string, MaterializedResultSet>` — CTE name → materialized rows + columns
