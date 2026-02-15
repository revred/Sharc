# Sharc Documentation

Welcome to the **Sharc Context Engine** documentation.

## 🚀 Start Here
*   [**Getting Started**](GETTING_STARTED.md): Zero to code in 5 minutes.
*   [**Cookbook**](COOKBOOK.md): 15 copypasta-ready recipes (Reading, Filtering, Graph, Trust).
*   [**FAQ**](FAQ.md): Common questions and answers.
*   [**When NOT to Use Sharc**](WHEN_NOT_TO_USE.md): Honest limitations.

## 📚 Core Features
*   [**Sharc Query (Sharq)**](ParsingTsql.md): The SQL-like query language reference (Syntax, Graph Arrows, CTEs).
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

## 🛠️ Contributing
*   [**Coding Standards**](CodingStandards.md): Rules for contributing to the alloc-free codebase.
*   [**Migration Guide**](MIGRATION.md): Coming from older versions.

---

### Internal Process
*   [**PRC (Process, Requests, Comments)**](../PRC/README.md): Design documents and engineering logs.
