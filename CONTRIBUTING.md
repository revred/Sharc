# Contributing to Sharc

Thank you for your interest in contributing to Sharc! This guide will help you get started.

## Getting Started

1. Fork and clone the repository
2. Ensure you have [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed
3. Build and run tests:

```bash
dotnet build
dotnet test
```

All 1,730 tests must pass before submitting a PR.

## Development Workflow

### TDD — Non-Negotiable

Every feature starts with tests. The cycle is:

1. Write failing test(s) that define behavior
2. Run — RED
3. Write minimum implementation to pass
4. Run — GREEN
5. Refactor
6. Run all tests — still GREEN

Never write implementation code without a corresponding test.

### Test Naming

```
[MethodUnderTest]_[Scenario]_[ExpectedResult]
```

Examples:
- `DecodeVarint_SingleByteZero_ReturnsZero`
- `Parse_InvalidMagic_ThrowsInvalidDatabaseException`

Use xUnit `Assert.*` methods only. No FluentAssertions.

## Code Style

- **`ReadOnlySpan<byte>` and `Span<byte>`** over `byte[]` in internal APIs
- **Zero allocation on hot paths**: no LINQ, no boxing, no string interpolation in tight loops
- **Big-endian reads**: use `BinaryPrimitives.ReadUInt16BigEndian()` etc.
- **`sealed`** on all classes unless designed for inheritance
- **Nullable reference types** enabled everywhere
- **`using` declarations** (not `using` blocks) for disposables in short-lived scopes
- **XML doc comments** on all public API members

## Project Structure

```text
src/Sharc/                    Public API + Write Engine + Trust Layer
src/Sharc.Core/               B-Tree, Records, Page I/O, Primitives
src/Sharc.Query/              SQL pipeline: parser, compiler, executor
src/Sharc.Crypto/             AES-256-GCM encryption, Argon2id KDF
src/Sharc.Graph/              Graph storage (ConceptStore, RelationStore)
src/Sharc.Graph.Surface/      Graph interfaces and models
src/Sharc.Arena.Wasm/         Live benchmark arena (Blazor WASM)
tests/Sharc.Tests/            1,003 unit tests
tests/Sharc.IntegrationTests/ 213 end-to-end tests
tests/Sharc.Query.Tests/      425 query pipeline tests
tests/Sharc.Graph.Tests.Unit/ 53 graph tests
tests/Sharc.Index.Tests/      22 index CLI tests
tests/Sharc.Context.Tests/    14 MCP context tests
bench/Sharc.Benchmarks/       BenchmarkDotNet suite (Sharc vs SQLite)
tools/Sharc.Context/          MCP Context Server
tools/Sharc.Index/            Git history → SQLite CLI
```

## Pull Request Process

1. Create a feature branch from `main`
2. Write tests first, then implementation
3. Ensure all tests pass: `dotnet test`
4. Ensure the build has zero warnings: `dotnet build --configuration Release`
5. Keep PRs focused — one feature or fix per PR
6. Write a clear PR description explaining **what** and **why**

## What NOT to Do

- **Do not add heavy dependencies** — Sharc is zero-dependency by design
- **Do not use `unsafe` code** unless profiling proves >20% gain
- **Do not allocate in hot paths** — use spans, stackalloc, ArrayPool
- **Do not break the public API surface** without discussion first

## Good First Issues

Look for issues labeled [`good first issue`](../../labels/good%20first%20issue). These are scoped, well-defined tasks suitable for newcomers:

- Adding missing XML doc comments on public types
- Writing additional test cases for edge conditions
- Improving error messages to be more actionable
- Small performance improvements with benchmark evidence

## Architecture Overview

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for a full breakdown of the layered design.

The key insight: Sharc reads SQLite pages directly through `ReadOnlySpan<byte>` slicing, bypassing SQLite's VDBE interpreter and P/Invoke boundary. The built-in SQL pipeline (SELECT, WHERE, ORDER BY, GROUP BY, UNION, Cote) compiles queries against the raw B-tree layer for maximum throughput.

## Questions?

Open a [Discussion](../../discussions) or file an [Issue](../../issues). We're happy to help.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
