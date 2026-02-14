# Ambition: Views (Pre-Computed Tables / Transforms)

**Evaluation Date:** February 14, 2026
**Inspiration:** SurrealDB's `DEFINE TABLE ... AS SELECT` pre-computed views
**Priority:** High — enables reporting, dashboards, derived analytics without external tools

---

## What This Feature Is

A **View** in Sharc is a named, optionally materialized transform that combines data from one or more tables using inference, math, formulas, or rules. It produces a virtual table that users can query like any other table.

Views serve as:
- **Aliases** for complex queries
- **Reports** that combine multi-table data
- **Transforms** that apply business logic (aggregation, scoring, ranking)
- **Materialized snapshots** for expensive analytics

---

## SurrealDB Reference Syntax

SurrealDB calls these "pre-computed table views" — event-based, incrementally updating, materialized views.

```surql
-- Define a view that aggregates reviews per product
DEFINE TABLE avg_product_review TYPE NORMAL AS
SELECT
    count()              AS number_of_reviews,
    math::mean(rating)   AS avg_review,
    ->product.id         AS product_id,
    ->product.name       AS product_name
FROM review
GROUP BY product_id, product_name;

-- Query the view like a normal table
SELECT * FROM avg_product_review;
```

**Key properties:**
- **Event-based**: Inserting/deleting from the source table triggers view update
- **Materialized**: First run executes the full query; result is stored
- **Incrementally updating**: Subsequent changes apply minimal delta operations
- **Limitations**: Only triggers from the `FROM` table; imports don't trigger updates

---

## Proposed SharcQL Syntax

Inspired by SurrealDB, adapted for Sharc's B-tree-native architecture:

```sharcql
-- Simple view: alias for a filtered scan
DEFINE VIEW active_users AS
SELECT id, name, email
FROM users
WHERE status = 'active';

-- Aggregation view: materialized summary
DEFINE VIEW order_summary AS
SELECT
    customer_id,
    count()          AS total_orders,
    sum(amount)      AS total_spent,
    avg(amount)      AS avg_order
FROM orders
GROUP BY customer_id;

-- Cross-table view with graph traversal (Sharc-native)
DEFINE VIEW agent_trust_scores AS
SELECT
    a.id             AS agent_id,
    a.name           AS agent_name,
    count(->attests)  AS attestation_count,
    avg(->attests.confidence) AS avg_confidence
FROM agents AS a
GROUP BY agent_id, agent_name;

-- Temporal view: rolling window
DEFINE VIEW recent_activity AS
SELECT *
FROM audit_log
WHERE created_at > time::now() - 24h;
```

### View Modes

```sharcql
-- Virtual (computed on read, no storage cost)
DEFINE VIEW my_view MODE VIRTUAL AS SELECT ...

-- Materialized (stored, updated on source writes)
DEFINE VIEW my_view MODE MATERIALIZED AS SELECT ...

-- Snapshot (stored, updated manually or on schedule)
DEFINE VIEW my_view MODE SNAPSHOT AS SELECT ...
```

---

## Implementation Effort Assessment

### What Sharc Has Today

| Capability | Status | Notes |
|-----------|--------|-------|
| B-tree sequential scan | Done | Full table scan with column projection |
| WHERE filtering | Done | 6 operators via FilterStar JIT |
| Schema parsing | Done | `SchemaReader` reads `sqlite_schema` |
| Record decoding | Done | Zero-alloc column projection |
| Write engine | Done | INSERT with B-tree mutations, ACID |

### What Needs to Be Built

| Component | Effort | Complexity | Description |
|-----------|--------|------------|-------------|
| **SharcQL parser** | 3-5 days | Medium | Tokenizer + recursive descent for SELECT/FROM/WHERE/GROUP BY |
| **Expression evaluator** | 2-3 days | Medium | `count()`, `sum()`, `avg()`, `min()`, `max()`, arithmetic |
| **GROUP BY engine** | 2-3 days | Medium | Hash-based grouping with accumulator functions |
| **View metadata storage** | 1 day | Low | Store view definitions in `sqlite_schema` as type="view" |
| **Virtual view execution** | 1 day | Low | Pipe query through scan → filter → project → group |
| **Materialized storage** | 3-4 days | High | Write results to a B-tree table; track source changes |
| **Incremental updates** | 5-7 days | High | Detect INSERT/DELETE on source, apply delta to view |
| **Cross-table views** | 3-4 days | High | Requires multi-cursor coordination (joins prerequisite) |

### Total Estimated Effort

| Scope | Days | What You Get |
|-------|------|-------------|
| **MVP: Virtual views** | 8-12 days | SELECT/WHERE/GROUP BY over single table, computed on read |
| **Phase 2: Materialized** | 12-18 days | Stored results, manual refresh |
| **Phase 3: Incremental** | 20-30 days | Auto-updating on source writes |
| **Full: Cross-table + graph** | 30-45 days | Multi-source views with graph traversal |

### Critical Dependencies

1. **SharcQL parser** — shared across Views, Joins, and FTS; build once, use everywhere
2. **Expression evaluator** — needed for GROUP BY aggregates and computed columns
3. **Write engine maturity** — materialized views require reliable INSERT/UPDATE/DELETE

### Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Parser complexity creep | High | Keep grammar minimal: SELECT/FROM/WHERE/GROUP BY only |
| SQLite schema compatibility | Medium | Store views as `CREATE VIEW` SQL in sqlite_schema |
| Incremental update correctness | High | Start with full recompute; optimize later |
| Memory pressure for large materializations | Medium | Stream results via cursor, don't buffer entire result set |

---

## Strategic Value

- **Sharc becomes queryable** — users don't need to write C# to extract insights
- **Trust layer reports** — reputation scores, attestation summaries become first-class views
- **Graph analytics** — path counts, centrality, cluster summaries as materialized views
- **WASM Arena integration** — views power dashboard slides without custom code

---

## Decision: Recommended Approach

Start with **MVP Virtual Views** (8-12 days). This gives users a SurrealDB-inspired query experience with zero storage overhead. Materialized views follow as write engine matures.

The SharcQL parser built for views becomes the foundation for Joins and Full-Text Search.
