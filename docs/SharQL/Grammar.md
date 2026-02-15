# SharQL Grammar Reference

SharQL is Sharc's query language — a SQL-inspired syntax with SurrealDB-style graph traversal using "shark tooth" edge operators (`|>`, `<|`, `<|>`).

## Statement Grammar

```ebnf
statement       ::= select_stmt ';'?

select_stmt     ::= SELECT [DISTINCT] select_list
                     FROM table_ref
                     [WHERE expr]
                     [GROUP BY expr_list]
                     [HAVING expr]
                     [ORDER BY order_list]
                     [LIMIT expr [OFFSET expr]]

select_list     ::= '*' | select_item (',' select_item)*
select_item     ::= expr [AS identifier]

table_ref       ::= identifier [AS identifier]
                   | identifier ':' identifier        (* record ID, e.g. person:alice *)

order_item      ::= expr [ASC | DESC]
order_list      ::= order_item (',' order_item)*
expr_list       ::= expr (',' expr)*
```

## Expression Grammar

Operators listed from lowest to highest precedence:

```ebnf
expr            ::= or_expr

or_expr         ::= and_expr (OR and_expr)*
and_expr        ::= not_expr (AND not_expr)*
not_expr        ::= [NOT] comparison

comparison      ::= addition (comp_op addition)?
                   | addition IS [NOT] NULL
                   | addition [NOT] IN '(' expr_list ')'
                   | addition [NOT] BETWEEN addition AND addition
                   | addition [NOT] LIKE addition
                   | addition match_op addition

comp_op         ::= '=' | '!=' | '<>' | '<' | '<=' | '>' | '>='
match_op        ::= '@@' | '@AND@' | '@OR@'

addition        ::= multiplication (('+' | '-') multiplication)*
multiplication  ::= unary (('*' | '/' | '%') unary)*
unary           ::= ['-'] primary

primary         ::= literal
                   | identifier ['.' identifier]
                   | function_call
                   | '(' expr ')'
                   | edge_expr
                   | record_id
                   | case_expr
                   | cast_expr
                   | parameter

case_expr       ::= CASE (WHEN expr THEN expr)+ [ELSE expr] END
cast_expr       ::= CAST '(' expr AS identifier ')'
parameter       ::= '$' identifier

edge_expr       ::= edge_step+ ['.' identifier | '.*']
edge_step       ::= '|>' identifier          (* forward traversal  *)
                   | '<|' identifier          (* backward traversal *)
                   | '<|>' identifier         (* bidirectional      *)

record_id       ::= identifier ':' identifier  (* e.g. person:alice *)

function_call   ::= identifier '(' [DISTINCT] [expr_list | '*'] ')'
```

## Lexical Grammar

```ebnf
literal         ::= INTEGER | FLOAT | STRING | TRUE | FALSE | NULL

identifier      ::= letter (letter | digit | '_')*
                   | '"' ... '"'       (* double-quoted *)
                   | '[' ... ']'       (* bracket-quoted *)
                   | '`' ... '`'       (* backtick-quoted *)

STRING          ::= "'" ... "'"       (* single-quoted, '' for escaping *)
INTEGER         ::= digit+
FLOAT           ::= digit+ '.' digit+ [('e'|'E') ['+' | '-'] digit+]

letter          ::= [a-zA-Z_]
digit           ::= [0-9]
```

## Keywords

All keywords are case-insensitive:

`SELECT`, `FROM`, `WHERE`, `GROUP`, `BY`, `ORDER`, `ASC`, `DESC`,
`LIMIT`, `OFFSET`, `AND`, `OR`, `NOT`, `IN`, `BETWEEN`, `LIKE`,
`IS`, `NULL`, `TRUE`, `FALSE`, `AS`, `DISTINCT`, `CASE`, `WHEN`,
`THEN`, `ELSE`, `END`, `HAVING`, `CAST`

## Edge Operators (Shark Tooth)

- **`|>`** — Forward Edge. Traverse relationship forward.
- **`<|`** — Back Edge. Traverse relationship backward.
- **`<|>`** — Bidi Edge. Traverse relationship in both directions.

### Examples

```sql
-- Forward traversal: who does Alice know?
SELECT |>knows|>person.* FROM person:alice

-- Backward traversal: who ordered this product?
SELECT <|order<|person.* FROM product:crystal_cave

-- Multi-hop: friends of friends who bought same product
SELECT |>order|>product<|order<|person FROM person:billy

-- Bidirectional: all friends
SELECT <|>friends_with<|>person.name FROM person:alice

-- Edge with scoring in WHERE
SELECT * FROM agents WHERE count(|>attests) > 3
```

## Full-Text Match Operators

| Operator | Description |
| -------- | ----------- |
| `@@` | Full-text match |
| `@AND@` | All terms must match |
| `@OR@` | Any term may match |

```sql
SELECT * FROM documents WHERE content @@ 'graph traversal'
SELECT * FROM documents WHERE content @AND@ 'B-tree cursor'
```

## CASE / CAST / Parameters

```sql
-- CASE expression
SELECT name,
       CASE WHEN age >= 18 THEN 'adult' ELSE 'minor' END AS category
FROM users

-- CAST expression
SELECT CAST(age AS TEXT) AS age_text FROM users

-- String concatenation with + (runtime type dispatch)
SELECT first_name + ' ' + last_name AS full_name FROM users

-- Parameter references
SELECT * FROM users WHERE age > $min_age LIMIT $page_size

-- SELECT DISTINCT
SELECT DISTINCT city FROM users

-- HAVING clause
SELECT department, count(*) AS cnt FROM employees
GROUP BY department HAVING count(*) > 5
ORDER BY cnt DESC
```
