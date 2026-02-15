# Sharq: The Sharc Query Language

**Sharq** (Sharc Query) is a SQL-like dialect designed for querying graph-relational data. It combines the familiarity of T-SQL with the power of graph traversal and modern analytical functions.

## 1. Basic Select

Standard SQL syntax is fully supported.

```sql
SELECT id, name, age
FROM users
WHERE age >= 18
ORDER BY age DESC
LIMIT 10 OFFSET 5;
```

### Supported Clauses
*   `SELECT [DISTINCT]`: Column selection and projection.
*   `FROM`: Source table or subquery.
*   `WHERE`: Filter predicates.
*   `GROUP BY`: Aggregation grouping.
*   `HAVING`: Filter on aggregates.
*   `ORDER BY`: Sort order (`ASC` / `DESC`), optionally `NULLS FIRST` / `NULLS LAST`.
*   `LIMIT` / `OFFSET`: Pagination.

## 2. Graph Traversal (Arrow Syntax)

Sharq introduces "Arrow Syntax" for expressive graph queries. You can traverse edges directly in the column list or WHERE clause.

### Syntax
*   `|>`: Forward edge (outgoing).
*   `<|`: Backward edge (incoming).
*   `<|>`: Bidirectional edge (any direction).

### Examples

**Get friends of a user:**
```sql
SELECT 
    name,
    id|>friend|>name AS friend_name
FROM users
WHERE id = 'alice';
```

**Filter by related entity properties:**
```sql
SELECT * FROM papers
WHERE id|>cites|>author|>name = 'Dr. Smith';
```

**Record ID Literals:**
You can start a traversal from a specific record ID using the `table:id` syntax:
```sql
SELECT users:alice|>friends|>name;
```

## 3. Common Table Expressions (CTEs)

Modularize complex queries using `WITH`.

```sql
WITH regional_sales AS (
    SELECT region, SUM(amount) as total
    FROM sales
    GROUP BY region
)
SELECT * FROM regional_sales
WHERE total > 100000;
```

## 4. Compound Operators

Combine results from multiple queries.

*   `UNION [ALL]`: Combine sets.
*   `INTERSECT`: Return only rows in both sets.
*   `EXCEPT`: Return rows in first set but not second.

```sql
SELECT id FROM users
INTERSECT
SELECT user_id FROM orders;
```

## 5. Window Functions

Perform calculations across a set of table rows that are somehow related to the current row.

```sql
SELECT 
    name,
    salary,
    RANK() OVER (PARTITION BY dept_id ORDER BY salary DESC) as rank
FROM employees;
```

**Supported Window Functions:**
*   `ROW_NUMBER()`
*   `RANK()`
*   `DENSE_RANK()`
*   `SUM()`, `AVG()`, `MIN()`, `MAX()`, `COUNT()`

## 6. Operators & Functions

### Comparisons
*   `=`, `!=` (or `<>`)
*   `<`, `<=`, `>`, `>=`
*   `IS [NOT] NULL`

### Pattern Matching
*   `LIKE 'pattern%'`
*   `MATCH`: Full-text search (if configured).

### Logical
*   `AND`, `OR`, `NOT`

### Sets & Ranges
*   `IN (1, 2, 3)`
*   `IN (SELECT id FROM ...)`
*   `BETWEEN x AND y`
*   `EXISTS (SELECT ...)`

### Control Flow
*   `CASE WHEN ... THEN ... ELSE ... END`
*   `CAST(expr AS type)`
