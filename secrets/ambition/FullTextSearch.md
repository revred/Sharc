# Ambition: Full-Text Search

**Evaluation Date:** February 14, 2026
**Inspiration:** SurrealDB's `DEFINE ANALYZER` + `DEFINE INDEX ... FULLTEXT ANALYZER` + BM25 scoring
**Priority:** Medium-High — enables search-driven applications, knowledge graph queries, context retrieval

---

## What This Feature Is

**Full-Text Search (FTS)** allows users to search text content using natural language queries instead of exact-match WHERE filters. It includes:
- **Tokenization**: Breaking text into searchable terms
- **Analyzers**: Lowercasing, stemming, stop-word removal, n-grams
- **Inverted index**: Mapping terms → document IDs for O(1) lookup
- **Relevance scoring**: BM25 / TF-IDF ranking
- **Highlighting**: Marking matched terms in results

---

## SurrealDB Reference Syntax

### Step 1: Define an Analyzer

```surql
DEFINE ANALYZER article_analyzer
    TOKENIZERS class, blank
    FILTERS lowercase, ascii, snowball(english);
```

**Available Tokenizers:**
- `blank` — split on whitespace
- `camel` — split camelCase into tokens
- `class` — split on unicode character class boundaries
- `punct` — split on punctuation

**Available Filters:**
- `lowercase` — normalize case
- `uppercase` — normalize case
- `ascii` — strip diacritics (e.g., cafe → cafe)
- `snowball(lang)` — stemming (running → run)
- `ngram(min, max)` — generate n-grams
- `edgengram(min, max)` — prefix n-grams (autocomplete)
- `mapper('file.txt')` — synonym/alias mapping

### Step 2: Define a Full-Text Index

```surql
DEFINE INDEX article_body ON TABLE article
    FIELDS body
    FULLTEXT ANALYZER article_analyzer
    BM25
    HIGHLIGHTS;
```

### Step 3: Search with Scoring and Highlighting

```surql
-- Basic search
SELECT * FROM article WHERE body @@ "machine learning";

-- AND/OR operators
SELECT * FROM document WHERE text @AND@ "personal rare";
SELECT * FROM document WHERE text @OR@ "personal nice";

-- With scoring and highlighting
SELECT
    title,
    search::highlight('<b>', '</b>', 0) AS snippet,
    search::score(0) AS relevance
FROM article
WHERE body @0@ "neural networks"
ORDER BY relevance DESC;
```

The numeric identifier (`@0@`) links the match operator to its corresponding `search::score(0)` and `search::highlight(_, _, 0)` function calls.

---

## Proposed SharcQL Syntax

### Analyzer Definition

```sharcql
-- Basic analyzer
DEFINE ANALYZER english_text
    TOKENIZERS blank, punct
    FILTERS lowercase, snowball(english);

-- Autocomplete analyzer
DEFINE ANALYZER autocomplete
    TOKENIZERS blank
    FILTERS lowercase, edgengram(2, 10);

-- Code analyzer (camelCase aware)
DEFINE ANALYZER code_search
    TOKENIZERS camel, punct
    FILTERS lowercase, ascii;
```

### Full-Text Index

```sharcql
-- Single-field index with BM25 scoring
DEFINE INDEX content_search ON TABLE documents
    FIELDS content
    FULLTEXT ANALYZER english_text
    BM25
    HIGHLIGHTS;

-- Multi-field (separate indices, same analyzer)
DEFINE INDEX title_search ON TABLE articles FIELDS title
    FULLTEXT ANALYZER english_text BM25;
DEFINE INDEX body_search ON TABLE articles FIELDS body
    FULLTEXT ANALYZER english_text BM25 HIGHLIGHTS;
```

### Search Queries

```sharcql
-- Simple match
SELECT * FROM documents WHERE content @@ "graph traversal";

-- Boolean operators
SELECT * FROM documents WHERE content @AND@ "B-tree cursor";
SELECT * FROM documents WHERE content @OR@ "encryption trust";

-- Scored and highlighted
SELECT
    title,
    search::score(0) AS score,
    search::highlight('<mark>', '</mark>', 0) AS snippet
FROM articles
WHERE body @0@ "zero allocation"
ORDER BY score DESC
LIMIT 10;

-- Cross-field search (query multiple indices)
SELECT
    title,
    search::score(0) + search::score(1) * 2.0 AS combined_score
FROM articles
WHERE title @0@ "sharc" OR body @1@ "performance"
ORDER BY combined_score DESC;
```

---

## Implementation Effort Assessment

### What Sharc Has Today

| Capability | Status | Notes |
|-----------|--------|-------|
| B-tree read/write | Done | Foundation for inverted index storage |
| Record encoding/decoding | Done | Can store term → posting list mappings |
| WHERE filtering | Done | Can integrate FTS as a filter predicate |
| Index B-tree reads | Done | `IndexBTreeCursor` for secondary index lookups |
| Write engine | Done | INSERT for building indices during data load |
| Schema system | Done | Can register new index types in `sqlite_schema` |

### What Needs to Be Built

| Component | Effort | Complexity | Description |
|-----------|--------|------------|-------------|
| **Tokenizer framework** | 2-3 days | Low | Interface + blank/punct/class/camel tokenizers |
| **Filter pipeline** | 2-3 days | Medium | Lowercase, ASCII normalization, n-gram generator |
| **Stemmer (Snowball)** | 3-5 days | High | Port/embed Snowball stemmer for English (+ other languages) |
| **Inverted index structure** | 3-4 days | Medium | Term → (docId, position, frequency) stored in B-tree pages |
| **Index builder** | 2-3 days | Medium | Scan source table → tokenize → build inverted index |
| **BM25 scorer** | 1-2 days | Low | Standard formula: term frequency, inverse doc frequency, doc length |
| **`@@` match operator** | 1-2 days | Low | Parse query terms, look up in inverted index, apply boolean logic |
| **Highlighting** | 1-2 days | Low | Find term positions in original text, wrap with markers |
| **AND/OR query parsing** | 1 day | Low | Boolean expression tree over terms |
| **Index maintenance (INSERT)** | 2-3 days | Medium | Update inverted index on new row insertion |
| **Index maintenance (DELETE)** | 3-4 days | High | Remove terms from inverted index on row deletion |
| **SharcQL parser extensions** | 1-2 days | Low | `DEFINE ANALYZER`, `DEFINE INDEX ... FULLTEXT`, `@@` operator |

### Total Estimated Effort

| Scope | Days | What You Get |
|-------|------|-------------|
| **MVP: Basic FTS** | 12-18 days | Tokenize + inverted index + exact match + basic ranking |
| **Phase 2: BM25 + highlighting** | 18-25 days | Relevance scoring, highlighted snippets, AND/OR |
| **Phase 3: Analyzers** | 25-35 days | Snowball stemming, n-grams, autocomplete |
| **Phase 4: Index maintenance** | 35-45 days | Auto-update on INSERT/DELETE, incremental rebuild |
| **Full: Production FTS** | 45-60 days | Multi-language, phrase queries, proximity search |

### Inverted Index Storage Design

The inverted index can be stored as a B-tree within the same SQLite file format:

```
Inverted Index B-Tree (one per indexed field):
┌──────────────┐
│ Key: term     │  (UTF-8 encoded, after analyzer pipeline)
│ Value: posting│  (varint-encoded list of docIds + positions)
└──────────────┘

Posting List Entry:
┌──────────┬───────────┬──────────┬──────────────────┐
│ docId    │ frequency │ fieldLen │ positions[]      │
│ (varint) │ (varint)  │ (varint) │ (varint-delta)   │
└──────────┴───────────┴──────────┴──────────────────┘
```

**Why B-tree storage works**: Sharc already has B-tree read/write. The inverted index is just another B-tree where keys are terms (sorted lexicographically) and values are posting lists. This means:
- Term lookup: O(log N) via `SeekFirst(term)`
- Prefix queries: range scan from `SeekFirst("pre")` to first term > "pre\xff"
- Storage: same page format, same caching, same encryption

### Stemmer Options

| Option | Effort | Quality | Notes |
|--------|--------|---------|-------|
| **No stemming (exact match)** | 0 days | Basic | Good enough for code search, IDs |
| **Port Snowball C# implementation** | 3-5 days | Good | Algorithm is well-documented; C# ports exist |
| **Algorithmic (Porter2)** | 2-3 days | Good | Simpler than Snowball, English-only |
| **Dictionary-based** | 5-7 days | Best | Load lemma dictionary, exact lookups |

**Recommendation**: Start with no stemming (MVP), add Porter2 in Phase 3.

### Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Inverted index size | Medium | Compress posting lists with varint-delta encoding |
| Index build time for large tables | Medium | Stream tokenization; don't buffer entire table |
| Stemmer correctness (false matches) | Low | Start without stemming; add as opt-in |
| SQLite FTS5 compatibility | Low | Don't try to be compatible; Sharc FTS is its own thing |
| Memory for large posting lists | Medium | Read posting lists via page cursor, not into memory |

---

## Competitive Landscape

| Engine | FTS Approach | Sharc Opportunity |
|--------|-------------|-------------------|
| **SQLite FTS5** | Virtual table, shadow tables, Porter tokenizer | Sharc can match features with zero native deps |
| **SurrealDB** | Built-in analyzers, BM25, highlighting | Sharc adopts same syntax; runs in 82KB |
| **DuckDB** | No built-in FTS | Sharc differentiates |
| **LiteDB** | Basic text index | Sharc can exceed with BM25 + analyzers |

---

## Strategic Value

- **Knowledge graph search** — find concepts by description, not just by ID
- **Trust layer audit** — search ledger entries by content ("who attested what about whom?")
- **Context retrieval for AI** — BM25 + graph traversal = structured RAG without vector DB
- **WASM Arena benchmark** — FTS comparison slide: Sharc vs SQLite FTS5 vs SurrealDB
- **Tiny footprint FTS** — full-text search in 82KB, no Lucene/Elastic dependency

---

## Decision: Recommended Approach

Start with **MVP: Basic FTS** (12-18 days). Build the inverted index as a B-tree (leveraging existing infrastructure), implement blank+lowercase tokenizer, exact-match `@@` operator, and basic TF-IDF scoring.

This provides immediate value for knowledge graph search and trust layer queries. BM25, stemming, and highlighting follow in subsequent phases.

**Key insight**: Sharc's B-tree infrastructure means the inverted index is ~60% already built. The posting list is just another record format. The term lookup is just another `SeekFirst()`. The hardest part is the analyzer pipeline, and even that starts simple (split on whitespace, lowercase).

### Shared Infrastructure with Views & Joins

| Shared Component | Views | Joins | FTS |
|-----------------|-------|-------|-----|
| SharcQL parser | SELECT/FROM/WHERE/GROUP BY | JOIN/ON | DEFINE ANALYZER/INDEX, @@ |
| Expression evaluator | Aggregates | Join conditions | Score functions |
| Multi-cursor coordination | Cross-table views | Join execution | Index + data table |
| Query planner | View refresh strategy | Join order | Index selection |

**Building all three features shares ~40% of the implementation effort.** The SharcQL parser, expression evaluator, and multi-cursor coordinator are common infrastructure.
