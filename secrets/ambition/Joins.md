# Ambition: Full Battery of SQL Joins

**Evaluation Date:** February 14, 2026
**Inspiration:** SurrealDB's record links + graph arrows as join replacement
**Priority:** High — foundational for cross-table queries, views, and graph analytics

---

## What This Feature Is

SQL Joins combine rows from two or more tables based on a related column. Traditional databases support INNER, LEFT, RIGHT, FULL OUTER, CROSS, and SELF joins. SurrealDB takes a different approach: it replaces joins entirely with **record links** (foreign key pointers) and **graph edges** (traversal arrows).

Sharc already has graph traversal (`->` edges via `RelationStore`). The question is: how much effort to support the full battery of traditional joins AND SurrealDB-style relationship queries?

---

## SurrealDB Reference: Joins Replaced by Three Models

### 1. Record Links (Replaces INNER/LEFT JOIN)

```surql
-- Instead of: SELECT * FROM review JOIN product ON review.product_id = product.id
-- SurrealDB stores the record ID directly:
CREATE review SET rating = 5, product = product:macbook;

-- Query traverses the link automatically:
SELECT rating, product.name, product.price FROM review;
-- Result: { rating: 5, product: { name: "MacBook", price: 1999 } }
```

### 2. Graph Edges (Replaces complex multi-table JOINs)

```surql
-- Create relationships
RELATE person:billy -> order -> product:crystal_cave SET quantity = 2;

-- Traverse forward (who ordered what?)
SELECT ->order->product.* FROM person:billy;

-- Traverse backward (who ordered this product?)
SELECT <-order<-person.* FROM product:crystal_cave;

-- Multi-hop (friends of friends who bought same product)
SELECT ->order->product<-order<-person FROM person:billy;
```

### 3. Bidirectional Traversal (Replaces SELF JOIN)

```surql
-- Symmetric relationships
RELATE person:alice <-> friends_with <-> person:bob;

-- Query both directions
SELECT *, <->friends_with<->person AS friends FROM person;
```

### 4. Recursive Graph Queries (Replaces recursive CTEs)

```surql
-- 3-level deep hierarchy
SELECT @.{3}.{ id, parents: ->child_of->person.@ } FROM person:1;
```

---

## Proposed SharcQL Syntax

### Traditional Join Syntax (SQL-compatible)

```sharcql
-- INNER JOIN
SELECT u.name, o.amount
FROM users AS u
JOIN orders AS o ON u.id = o.user_id;

-- LEFT JOIN (include users with no orders)
SELECT u.name, o.amount
FROM users AS u
LEFT JOIN orders AS o ON u.id = o.user_id;

-- CROSS JOIN (cartesian product)
SELECT a.name, b.name
FROM team_a AS a
CROSS JOIN team_b AS b;

-- SELF JOIN (hierarchy)
SELECT e.name AS employee, m.name AS manager
FROM employees AS e
JOIN employees AS m ON e.manager_id = m.id;
```

### SurrealDB-Style Arrow Syntax (Sharc-native)

```sharcql
-- Forward traversal (uses existing graph layer)
SELECT ->has_order->orders.amount FROM users:42;

-- Backward traversal
SELECT <-has_order<-users.name FROM orders:100;

-- Multi-hop
SELECT ->knows->person->wrote->article.title FROM person:alice;

-- Record links (automatic foreign key follow)
SELECT name, department.name AS dept FROM employees;
```

### Hybrid Queries

```sharcql
-- Combine traditional WHERE with graph traversal
SELECT u.name, count(->placed->orders) AS order_count
FROM users AS u
WHERE u.status = 'active'
GROUP BY u.name;
```

---

## Implementation Effort Assessment

### What Sharc Has Today

| Capability | Status | Notes |
|-----------|--------|-------|
| B-tree cursor (sequential) | Done | `BTreeCursor` with `MoveNext()` |
| B-tree cursor (seek) | Done | `SeekFirst(key)` for O(log N) point lookup |
| Index B-tree reads | Done | `IndexBTreeCursor` for secondary indices |
| Graph traversal | Done | `ConceptStore` + `RelationStore` + `EdgeCursor` |
| Arrow semantics (`->`, `<-`) | Done | `GetEdges()`, `GetIncomingEdges()`, `Traverse()` |
| WHERE filtering | Done | `SharcFilter` with 6 operators |

### What Needs to Be Built

| Component | Effort | Complexity | Description |
|-----------|--------|------------|-------------|
| **SharcQL parser (JOIN clause)** | 2-3 days | Medium | Extend parser with JOIN/ON/LEFT/RIGHT/CROSS grammar |
| **Nested loop join** | 2 days | Low | For each row in A, scan B for matches. Simple, correct. |
| **Hash join** | 3-4 days | Medium | Build hash table on smaller table, probe with larger. Fast for equi-joins. |
| **Sort-merge join** | 3-4 days | Medium | Sort both inputs, merge. Efficient for pre-sorted B-tree scans. |
| **Index-nested-loop join** | 2-3 days | Medium | Use B-tree seek on inner table. Leverages existing `SeekFirst()`. |
| **LEFT/RIGHT/FULL OUTER logic** | 2 days | Low | Track unmatched rows, emit NULLs for missing side |
| **CROSS JOIN** | 0.5 day | Low | Cartesian product, no condition needed |
| **SELF JOIN** | 0.5 day | Low | Same-table join with aliasing |
| **Record link resolution** | 2-3 days | Medium | Follow integer FK → seek target table → inline fields |
| **Arrow syntax in SharcQL** | 1-2 days | Low | Parse `->edge->target` into graph cursor operations |
| **Query optimizer (basic)** | 3-5 days | High | Choose join strategy based on table sizes, available indices |
| **Multi-way joins** | 2-3 days | Medium | Chain 2-way joins for 3+ table queries |

### Total Estimated Effort

| Scope | Days | What You Get |
|-------|------|-------------|
| **MVP: Nested-loop INNER JOIN** | 5-7 days | Two-table equi-join with WHERE filtering |
| **Phase 2: All join types** | 12-18 days | LEFT/RIGHT/FULL/CROSS/SELF + hash join |
| **Phase 3: Arrow queries** | 15-22 days | SurrealDB-style `->edge->target` in SharcQL |
| **Phase 4: Optimizer** | 22-30 days | Index selection, join reordering, cost estimation |
| **Full: Multi-way + recursive** | 35-50 days | N-table joins, recursive CTEs, graph recursion |

### Strategic Advantage: Sharc's Existing Graph Layer

Sharc already has the hard part: **bidirectional edge traversal with O(log N) seek**. The `EdgeCursor`, `ConceptStore`, and `RelationStore` are production-grade. Adding arrow syntax to SharcQL is wiring, not inventing.

Traditional databases add graph capabilities on top of relational joins. Sharc does the opposite: it has graph natively and adds relational joins. This is a stronger foundation because graph traversal is the harder problem.

### Join Strategy Decision Matrix

| Strategy | Best When | Sharc Advantage |
|----------|-----------|-----------------|
| **Nested loop** | Small inner table (<1000 rows) | Simple, always correct |
| **Index nested loop** | Inner table has B-tree index | Leverages existing `SeekFirst()` — free |
| **Hash join** | Large tables, equi-join, no index | Needs hash table builder |
| **Sort-merge** | Both inputs sorted (B-tree scan order) | B-tree cursors already sorted by rowid |
| **Graph traversal** | Relationship queries | Already built — `EdgeCursor` |

### Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Join performance on large tables | High | Start with index-nested-loop; B-tree seek is fast |
| Memory pressure for hash joins | Medium | Use streaming hash with spill-to-disk |
| Query optimizer complexity | High | Start with heuristic rules, not cost-based |
| NULL semantics in OUTER joins | Low | Follow SQL standard strictly |
| Parser grammar ambiguity | Medium | Keep grammar LL(1); no backtracking needed |

---

## Strategic Value

- **Sharc becomes a query engine** — not just a reader, but a tool for data exploration
- **Trust layer cross-referencing** — join agent attestations with ledger entries for audit
- **Graph + relational fusion** — unique positioning: one engine, both models
- **WASM Arena slides** — join benchmarks demonstrate Sharc's multi-model capability

---

## Decision: Recommended Approach

Start with **MVP: Index-Nested-Loop INNER JOIN** (5-7 days). This leverages Sharc's existing B-tree seek for O(log N) inner-table lookups — making it immediately competitive with SQLite's query planner for indexed joins.

The SharcQL parser built here is shared with Views and Full-Text Search.

**Key insight**: Sharc's B-tree cursor is already a sorted iterator. Sort-merge join comes nearly free. And the graph `EdgeCursor` already implements the hardest join pattern (relationship traversal). The effort is mostly in the query parser and planner, not in the data access.
