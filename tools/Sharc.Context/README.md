# Sharc.Context — MCP Context Server

A [Model Context Protocol](https://modelcontextprotocol.io/) server that gives AI assistants (Claude Code, VS Code Copilot, etc.) real-time access to Sharc's build, test, benchmark, and database query infrastructure.

## Tools Exposed

| Tool | Description |
|------|-------------|
| `RunTests` | Run unit/integration tests with optional filter and scope. Streams output incrementally. |
| `BuildCheck` | Quick build check — returns warnings/errors. |
| `TestStatus` | Fast test summary — pass/fail/skip counts. |
| `RunBenchmarks` | Run BenchmarkDotNet comparative benchmarks with filter and job type (short/medium/dry). |
| `ReadBenchmarkResults` | Read latest benchmark results from artifacts. |
| `ListBenchmarkResults` | List available benchmark result files. |
| `ProjectHealth` | Comprehensive snapshot: git status, file counts, build status. |
| `ReadFile` | Read any source file by relative path. |
| `SearchCode` | Search source code with regex patterns via git grep. |
| `ListSchema` | List all tables and columns in a SQLite database file. |
| `GetRowCount` | Get the total row count for a specific table. |
| `QueryTable` | Query a table with optional projection and filters (up to 100 rows). |
| `SeekRow` | Seek to a specific row by rowid. |

## Usage with Claude Code

The server is auto-configured via `.claude/settings.json`. When Claude Code starts in the Sharc directory, it automatically connects to this MCP server.

**Manual start:**
```bash
dotnet run --project tools/Sharc.Context
```

**Test it with MCP inspector:**
```bash
npx @anthropic/mcp-inspector dotnet run --project tools/Sharc.Context
```

## How It Works

The server uses stdio transport (JSON-RPC over stdin/stdout). When Claude calls a tool like `RunTests`, the server:

1. Spawns `dotnet test` as a child process
2. Captures stdout/stderr incrementally
3. Returns the complete output when done

For database query tools (`ListSchema`, `QueryTable`, etc.), the server uses the Sharc library directly to read SQLite files — no subprocess needed.

## Architecture

```
Claude Code <-> JSON-RPC (stdio) <-> Sharc.Context
                                        |-- TestRunnerTool (dotnet test)
                                        |-- BenchmarkTool (dotnet run benchmarks)
                                        |-- ProjectStatusTool (git, file I/O)
                                        +-- ContextQueryTool (Sharc library)
```
