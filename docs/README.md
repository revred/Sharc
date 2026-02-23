# Sharc Documentation

Welcome to the **Sharc Context Engine** documentation.

## 🚀 Start Here
*   [**Getting Started**](GETTING_STARTED.md): Zero to code in 5 minutes.
*   [**Cookbook**](COOKBOOK.md): 15 copypasta-ready recipes (Reading, Filtering, Graph, Trust).
*   [**FAQ**](FAQ.md): Common questions and answers.
*   [**When NOT to Use Sharc**](WHEN_NOT_TO_USE.md): Honest limitations.

## 📚 Core Features
*   [**Sharc Query (Sharq)**](ParsingTsql.md): The SQL-like query language reference (Syntax, Graph Arrows, Cotes).
*   [**Deep Dive: Parsing**](DeepDive_Parsing.md): How Sharq achieves zero-allocation parsing with SIMD.
*   [**BakedFilter JIT**](BakedFilter.md): The internal engine that compiles your queries to machine code.
*   [**Distributed Trust**](DistributedTrustArchitecture.md): **(Critical)** The architecture for Agent Identity, Ledgers, and decentralized AI memory.

## ⚙️ Architecture & Internals
*   [**Architecture**](ARCHITECTURE.md): High-level system design and layer breakdown.
*   [**File Format**](FileFormatQuickRef.md): Binary layout reference for the `sharc` / `sqlite` file format.
*   [**Comparison vs. MCP**](SharcVsMCP.md): Why Sharc is the "State" to MCP's "Protocol".
*   [**SQLite Analysis**](SQLiteAnalysis.md): Detailed comparison of Sharc vs. System.Data.SQLite.

## 📊 Performance
*   [**Benchmarks**](BENCHMARKS.md): Methodology and results vs. SQLite, DuckDB, and others.

## 🔀 Comparisons & Alternatives

*   [**Alternatives**](ALTERNATIVES.md): Honest comparison vs SQLite, LiteDB, DuckDB, SQLitePCLRaw.
*   [**Graph DB Comparison**](GRAPH_DB_COMPARISON.md): Sharc vs SurrealDB, ArangoDB, Neo4j — when to use which.
*   [**Integration Recipes**](INTEGRATION_RECIPES.md): 10 copy-paste integration recipes.
*   [**API Quick Reference**](API_QUICK_REFERENCE.md): Full accessor and method table.

## 🧠 JitSQL & Vector Search

*   [**JitSQL Cross-Language**](JITSQL_CROSS_LANGUAGE.md): JitSQL patterns mapped to Prisma, SQLAlchemy, Knex, GORM.
*   [**Vector Search**](VECTOR_SEARCH.md): Embedding storage, similarity search, RAG patterns, comparison vs Pinecone/pgvector.

## 🛠️ Contributing
*   [**Coding Standards**](CodingStandards.md): Rules for contributing to the alloc-free codebase.
*   [**Migration Guide**](MIGRATION.md): Coming from older versions.

---

### Internal Process
*   [**PRC (Process, Requests, Comments)**](../PRC/README.md): Design documents and engineering logs.
