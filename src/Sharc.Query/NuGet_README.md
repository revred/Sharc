# Sharc.Query

**SQL query pipeline for the Sharc database engine.**

Parser, compiler, and executor for SQL queries over SQLite files — SELECT, WHERE, JOIN, GROUP BY, ORDER BY, UNION, INTERSECT, EXCEPT, and aggregates. Pure C#, zero native dependencies.

## Features

- **SQL Parser**: Tokenizer and recursive-descent parser for a practical SQL subset.
- **Query Compiler**: Compiles parsed SQL into an optimized execution plan.
- **Join Engine**: Hash-join and nested-loop join with predicate pushdown.
- **Aggregation**: COUNT, SUM, AVG, MIN, MAX with GROUP BY and HAVING support.
- **Set Operations**: UNION, UNION ALL, INTERSECT, EXCEPT with type coercion.
- **ORDER BY / LIMIT**: In-memory sort with configurable collation.

## Note

This is an internal infrastructure package. Most users should reference the top-level [`Sharc`](https://www.nuget.org/packages/Sharc) package instead, which provides the `db.Query()` API surface.

[Full Documentation](https://github.com/revred/Sharc)
