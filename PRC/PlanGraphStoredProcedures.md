# PlanGraphStoredProcedures — Graph Stored Procedures for Ultra-Fast Ontology Processing

**Status**: Proposed
**Priority**: High — Second most requested feature
**Target**: v1.3.0
**Depends on**: Sharc.Graph (existing), TraversalPolicy (existing)

---

## Problem Statement

Sharc.Graph provides powerful low-level primitives (BFS traversal, zero-alloc edge cursors, TraversalPolicy). But users building AI agents, knowledge graphs, and ontology processors face a gap:

1. **Repetitive traversal patterns** — The same "find 2-hop method callers" or "expand context around a concept" patterns are coded repeatedly
2. **No composition** — Can't chain traversals (e.g., "find all files → find their methods → find callers of those methods")
3. **No pre-compilation** — Each `Traverse()` call re-evaluates the same decisions about index selection, direction, and filtering
4. **No cost control** — No way to define a budget (tokens, time, allocations) and have the engine optimize within that budget
5. **No reusable recipes** — Teams can't share graph query patterns as first-class objects

**Goal**: Define a **GraphProcedure** abstraction — pre-compiled, parameterized, composable graph operations that execute at native Sharc speed.

---

## Current Architecture

```
SharcContextGraph
├── Traverse(NodeKey, TraversalPolicy) → GraphResult
│   ├── Phase 1: Edge-only BFS (zero-alloc edge cursors)
│   └── Phase 2: Batch node resolution (ConceptStore lookups)
├── GetNode(NodeKey) → GraphRecord?
├── GetEdgeCursor(NodeKey, RelationKind?) → IEdgeCursor
│   ├── IndexEdgeCursor (preferred — B-tree index seek)
│   └── TableScanEdgeCursor (fallback)
└── Path reconstruction (parent-pointer, O(N) memory)
```

**TraversalPolicy** (current configuration):
- `MaxFanOut` — Hub capping per node
- `TargetTypeFilter` — Filter by ConceptKind
- `Kind` — Filter by RelationKind
- `Direction` — Outgoing / Incoming / Both
- `MaxDepth` — Hop limit
- `MaxTokens` — Token budget
- `MinWeight` — Edge quality threshold
- `Timeout` — Execution deadline
- `IncludePaths` — Path reconstruction
- `StopAtKey` — Early termination

**What's missing**: Composition, pre-compilation, multi-step pipelines, custom predicates, and reusable procedure definitions.

---

## Design: Graph Stored Procedures

### Core Concept

A **GraphProcedure** is a pre-compiled, parameterized graph operation that:
- Defines a **traversal plan** (one or more traversal steps)
- Accepts **parameters** at execution time (start key, filters, budgets)
- Is **compiled** once and **executed** many times
- Reports **cost** (nodes visited, edges scanned, tokens consumed, time elapsed)
- Can be **composed** into pipelines

### Three Tiers

| Tier | Name | Complexity | Example |
| :--- | :--- | :--- | :--- |
| 1 | **Simple Procedures** | Single traversal with fixed policy | "Expand 2 hops from start, Calls edges only" |
| 2 | **Filtered Procedures** | Traversal + node/edge predicates | "Find all Methods that call start node, weight > 0.5" |
| 3 | **Pipeline Procedures** | Multi-step chained traversals | "Find Files → their Methods → callers of those Methods" |

---

## API Design

### Tier 1: Simple Procedures (Pre-Compiled TraversalPolicy)

```csharp
// Define once
var expandContext = GraphProcedure.Create("ExpandContext")
    .Traverse(TraversalDirection.Both)
    .MaxDepth(2)
    .MaxFanOut(10)
    .MaxTokens(2000)
    .IncludePaths()
    .Build();

// Execute many times with different start nodes
GraphResult result1 = expandContext.Execute(graph, new NodeKey(100));
GraphResult result2 = expandContext.Execute(graph, new NodeKey(200));
```

**Implementation**: Essentially a frozen `TraversalPolicy` + pre-computed index selection hints.

### Tier 2: Filtered Procedures (With Predicates)

```csharp
var findCallers = GraphProcedure.Create("FindCallers")
    .Traverse(TraversalDirection.Incoming, RelationKind.Calls)
    .FilterNodes(node => node.Kind == ConceptKind.Method)
    .FilterEdges(edge => edge.Weight >= 0.5f)
    .MaxDepth(3)
    .Build();

GraphResult methods = findCallers.Execute(graph, targetMethodKey);
```

**Implementation**: Predicates compiled as delegates, pushed down to the BFS loop. Node filter applied in Phase 2 (batch resolution). Edge filter applied in Phase 1 (edge cursor loop).

### Tier 3: Pipeline Procedures (Multi-Step)

```csharp
var contextPipeline = GraphProcedure.CreatePipeline("DeepContext")
    .Step("FindContainingFile",
        step => step
            .Traverse(TraversalDirection.Incoming, RelationKind.Contains)
            .FilterNodes(node => node.Kind == ConceptKind.File)
            .MaxDepth(1))
    .Step("ExpandFileContents",
        step => step
            .Traverse(TraversalDirection.Outgoing, RelationKind.Defines)
            .MaxDepth(1))
    .Step("FindCallers",
        step => step
            .Traverse(TraversalDirection.Incoming, RelationKind.Calls)
            .MaxDepth(2)
            .MaxFanOut(5))
    .MergeStrategy(PipelineMerge.Union)  // Combine results from all steps
    .TotalBudget(maxTokens: 4000, timeout: TimeSpan.FromSeconds(1))
    .Build();

GraphResult context = contextPipeline.Execute(graph, startNodeKey);
```

**Implementation**: Each step produces a set of `NodeKey`s. The next step uses those as start nodes. The pipeline tracks a global budget across all steps.

---

## Core Types

### `IGraphProcedure`

```csharp
/// <summary>
/// A pre-compiled, parameterized graph operation.
/// </summary>
public interface IGraphProcedure
{
    /// <summary>Procedure name for registry lookup and diagnostics.</summary>
    string Name { get; }

    /// <summary>Estimated cost tier (0 = trivial, 3 = heavy).</summary>
    int EstimatedCostTier { get; }

    /// <summary>Execute against a graph with a single start node.</summary>
    GraphResult Execute(IContextGraph graph, NodeKey startKey);

    /// <summary>Execute against a graph with multiple start nodes (batch).</summary>
    GraphResult Execute(IContextGraph graph, ReadOnlySpan<NodeKey> startKeys);

    /// <summary>Execute with a cost observer for profiling.</summary>
    GraphResult Execute(IContextGraph graph, NodeKey startKey, ICostObserver? observer);
}
```

### `GraphProcedure` (Static Builder)

```csharp
public static class GraphProcedure
{
    /// <summary>Create a single-step procedure.</summary>
    public static GraphProcedureBuilder Create(string name);

    /// <summary>Create a multi-step pipeline procedure.</summary>
    public static GraphPipelineBuilder CreatePipeline(string name);

    /// <summary>Built-in procedures for common patterns.</summary>
    public static class BuiltIn
    {
        public static IGraphProcedure ExpandContext(int maxDepth = 2, int maxTokens = 2000);
        public static IGraphProcedure FindCallers(RelationKind kind = RelationKind.Calls);
        public static IGraphProcedure FindDefinitions(ConceptKind kind = ConceptKind.Method);
        public static IGraphProcedure ShortestPath(NodeKey from, NodeKey to);
        public static IGraphProcedure SubgraphExtract(int maxDepth = 3);
    }
}
```

### `GraphProcedureBuilder`

```csharp
public sealed class GraphProcedureBuilder
{
    public GraphProcedureBuilder Traverse(TraversalDirection direction, RelationKind? kind = null);
    public GraphProcedureBuilder FilterNodes(Func<GraphRecord, bool> predicate);
    public GraphProcedureBuilder FilterEdges(Func<IEdgeCursor, bool> predicate);
    public GraphProcedureBuilder MaxDepth(int depth);
    public GraphProcedureBuilder MaxFanOut(int fanOut);
    public GraphProcedureBuilder MaxTokens(int tokens);
    public GraphProcedureBuilder MinWeight(float weight);
    public GraphProcedureBuilder Timeout(TimeSpan timeout);
    public GraphProcedureBuilder IncludePaths(bool include = true);
    public GraphProcedureBuilder IncludeData(bool include = true);
    public IGraphProcedure Build();
}
```

### `GraphPipelineBuilder`

```csharp
public sealed class GraphPipelineBuilder
{
    public GraphPipelineBuilder Step(string name, Action<GraphProcedureBuilder> configure);
    public GraphPipelineBuilder MergeStrategy(PipelineMerge strategy);
    public GraphPipelineBuilder TotalBudget(int? maxTokens = null, TimeSpan? timeout = null);
    public IGraphProcedure Build();
}

public enum PipelineMerge
{
    /// <summary>Union all results from all steps.</summary>
    Union,
    /// <summary>Only include nodes found in all steps.</summary>
    Intersection,
    /// <summary>Each step feeds into the next (sequential expansion).</summary>
    Sequential
}
```

### `ICostObserver`

```csharp
/// <summary>
/// Observes execution costs for profiling and budgeting.
/// </summary>
public interface ICostObserver
{
    void OnNodesVisited(int count);
    void OnEdgesScanned(int count);
    void OnTokensConsumed(int count);
    void OnStepCompleted(string stepName, TimeSpan elapsed);
}

/// <summary>Default implementation that accumulates metrics.</summary>
public sealed class CostAccumulator : ICostObserver
{
    public int TotalNodesVisited { get; }
    public int TotalEdgesScanned { get; }
    public int TotalTokensConsumed { get; }
    public TimeSpan TotalElapsed { get; }
    public IReadOnlyList<StepCost> StepCosts { get; }
}
```

### `GraphProcedureRegistry`

```csharp
/// <summary>
/// Named procedure registry for lookup by string key.
/// </summary>
public sealed class GraphProcedureRegistry
{
    public void Register(IGraphProcedure procedure);
    public IGraphProcedure? Get(string name);
    public IReadOnlyList<string> ListNames();

    /// <summary>Register all built-in procedures.</summary>
    public void RegisterBuiltIns();
}
```

---

## Built-In Procedures (Ship with Library)

### 1. ExpandContext

**Purpose**: Standard AI agent context expansion — find relevant nodes around a starting point.

```csharp
// Implementation
GraphProcedure.Create("ExpandContext")
    .Traverse(TraversalDirection.Both)
    .MaxDepth(2)
    .MaxFanOut(10)
    .MaxTokens(2000)
    .IncludePaths()
    .Build();
```

**Use case**: AI agent asks "what's relevant around this method?"

### 2. FindCallers

**Purpose**: Reverse dependency analysis — who calls this?

```csharp
GraphProcedure.Create("FindCallers")
    .Traverse(TraversalDirection.Incoming, RelationKind.Calls)
    .FilterNodes(n => n.Kind == ConceptKind.Method || n.Kind == ConceptKind.Class)
    .MaxDepth(3)
    .Build();
```

### 3. FindDefinitions

**Purpose**: Forward analysis — what does this file/class define?

```csharp
GraphProcedure.Create("FindDefinitions")
    .Traverse(TraversalDirection.Outgoing, RelationKind.Defines)
    .MaxDepth(1)
    .Build();
```

### 4. ShortestPath

**Purpose**: Find the shortest path between two nodes.

```csharp
GraphProcedure.Create("ShortestPath")
    .Traverse(TraversalDirection.Both)
    .MaxDepth(6)
    .StopAt(targetKey)
    .IncludePaths()
    .Build();
```

### 5. SubgraphExtract

**Purpose**: Extract a self-contained subgraph for export or analysis.

```csharp
GraphProcedure.Create("SubgraphExtract")
    .Traverse(TraversalDirection.Both)
    .MaxDepth(3)
    .IncludeData()
    .IncludePaths()
    .Build();
```

### 6. OntologyClassify

**Purpose**: Classify a node by its relationships — is it a "hub" (many outgoing), a "leaf" (no outgoing), an "authority" (many incoming)?

```csharp
GraphProcedure.CreatePipeline("OntologyClassify")
    .Step("OutgoingFanout", s => s
        .Traverse(TraversalDirection.Outgoing)
        .MaxDepth(1))
    .Step("IncomingFanout", s => s
        .Traverse(TraversalDirection.Incoming)
        .MaxDepth(1))
    .MergeStrategy(PipelineMerge.Union)
    .Build();
```

---

## Execution Model

### Single-Step Execution

```
IGraphProcedure.Execute(graph, startKey)
    │
    ├── 1. Resolve start node (ConceptStore.GetNode)
    ├── 2. Build TraversalPolicy from compiled config
    ├── 3. Call graph.Traverse(startKey, policy)
    │       ├── Phase 1: Edge BFS (uses compiled index hints)
    │       └── Phase 2: Batch node resolution (applies node filter)
    ├── 4. Apply post-filters (if any)
    └── 5. Return GraphResult + cost metrics
```

### Pipeline Execution

```
IGraphProcedure.Execute(graph, startKey)
    │
    ├── Step 1: Execute first procedure → NodeKey[]
    ├── Step 2: Execute second procedure with Step 1 results as start keys → NodeKey[]
    ├── Step 3: Execute third procedure with Step 2 results → NodeKey[]
    ├── ...
    ├── Merge: Apply MergeStrategy (Union/Intersection/Sequential)
    ├── Budget check: Abort if total tokens/time exceeded
    └── Return merged GraphResult + per-step cost metrics
```

### Budget Enforcement

```csharp
// Global budget across all steps
if (observer.TotalTokensConsumed >= budget.MaxTokens)
    break;  // Stop expanding, return partial results

if (stopwatch.Elapsed >= budget.Timeout)
    break;  // Time's up, return what we have
```

Budget enforcement reuses the existing TraversalPolicy timeout/token mechanisms. The pipeline adds a *global* budget that supersedes per-step budgets.

---

## Performance Targets

| Operation | Target | Notes |
| :--- | :--- | :--- |
| Procedure.Build() | < 1 us | Just freezes config, no heavy work |
| Simple procedure (1 step, 2 hops) | Same as Traverse() | Zero overhead vs direct call |
| Filtered procedure (predicates) | < 5% overhead | Delegate invocation cost |
| Pipeline (3 steps) | < 3x single step | Sequential, not multiplicative |
| Batch execution (10 start keys) | < 10x single | No cross-key optimization yet |
| CostAccumulator overhead | < 1% | Counter increments only |

### Zero-Allocation Goal

- Procedure objects: allocated once at `Build()`, reused forever
- Execution: same allocation profile as current `Traverse()` (Tier 2-3)
- Pipeline buffers: `ArrayPool<NodeKey>` for intermediate key sets
- Cost observer: single object, counter fields only

---

## Implementation Plan

### Phase 1: Core Abstraction + Simple Procedures

**New files**:
- `src/Sharc.Graph.Surface/IGraphProcedure.cs`
- `src/Sharc.Graph.Surface/ICostObserver.cs`
- `src/Sharc.Graph.Surface/Model/PipelineMerge.cs`
- `src/Sharc.Graph/Procedures/GraphProcedure.cs` (static builder)
- `src/Sharc.Graph/Procedures/GraphProcedureBuilder.cs`
- `src/Sharc.Graph/Procedures/SimpleGraphProcedure.cs`
- `src/Sharc.Graph/Procedures/CostAccumulator.cs`

**Deliverables**:
- `GraphProcedure.Create().Build()` produces `IGraphProcedure`
- `Execute()` delegates to `SharcContextGraph.Traverse()`
- All existing TraversalPolicy fields supported
- `CostAccumulator` tracks nodes, edges, tokens, time
- Unit tests for each builder method

### Phase 2: Filtered Procedures

**New files**:
- `src/Sharc.Graph/Procedures/FilteredGraphProcedure.cs`

**Deliverables**:
- `FilterNodes()` and `FilterEdges()` predicates
- Node filter applied in Phase 2 (post-resolution)
- Edge filter injected into BFS Phase 1 loop
- Edge filter requires minor refactoring of `SharcContextGraph.Traverse()` to accept an edge predicate callback

### Phase 3: Pipeline Procedures

**New files**:
- `src/Sharc.Graph/Procedures/GraphPipelineBuilder.cs`
- `src/Sharc.Graph/Procedures/PipelineGraphProcedure.cs`

**Deliverables**:
- Multi-step chained execution
- `PipelineMerge.Sequential` (default): output keys of step N become input to step N+1
- `PipelineMerge.Union`: all results merged
- `PipelineMerge.Intersection`: only common nodes
- Global budget enforcement across steps
- Per-step cost reporting

### Phase 4: Built-In Library + Registry

**New files**:
- `src/Sharc.Graph/Procedures/BuiltInProcedures.cs`
- `src/Sharc.Graph/Procedures/GraphProcedureRegistry.cs`

**Deliverables**:
- 6 built-in procedures (ExpandContext, FindCallers, FindDefinitions, ShortestPath, SubgraphExtract, OntologyClassify)
- Registry with string-based lookup
- `RegisterBuiltIns()` convenience method

### Phase 5: Benchmarks + Optimization

**Deliverables**:
- Benchmark: Procedure vs raw Traverse() overhead
- Benchmark: Pipeline 3-step vs 3 sequential Traverse() calls
- Benchmark: Filtered vs unfiltered traversal
- Optimize: Pre-compute index hints at Build() time
- Optimize: Pool intermediate NodeKey arrays in pipelines

---

## Integration with Existing API

### Backwards Compatible

```csharp
// Old way (still works, no changes)
var result = graph.Traverse(startKey, new TraversalPolicy { MaxDepth = 2 });

// New way (procedures)
var proc = GraphProcedure.Create("MyTraversal").MaxDepth(2).Build();
var result = proc.Execute(graph, startKey);
```

### IContextGraph Extension

```csharp
// Optional convenience method on IContextGraph
public static class ContextGraphExtensions
{
    public static GraphResult Execute(
        this IContextGraph graph,
        IGraphProcedure procedure,
        NodeKey startKey,
        ICostObserver? observer = null)
    {
        return procedure.Execute(graph, startKey, observer);
    }
}
```

---

## Future Extensions (Not in Scope)

These are deferred but the architecture supports them:

1. **Graph Query Language (GQL)**: A Gremlin/Cypher-like DSL that compiles to procedures
   ```
   START n = NodeKey($startKey)
   MATCH n -[:Calls]-> m -[:Defines]-> c
   WHERE m.Kind = "Method" AND c.Weight > 0.5
   RETURN n, m, c
   ```

2. **Materialized Views**: Pre-compute and cache common subgraph patterns
3. **Parallel Execution**: Execute pipeline steps on different threads
4. **Procedure Serialization**: Save/load procedures as JSON for sharing
5. **Query Planner**: Cost-based optimization of pipeline step ordering

---

## Testing Strategy

### Unit Tests (Sharc.Graph.Tests.Unit)

```
GraphProcedureBuilder_MaxDepth_SetsPolicy
GraphProcedureBuilder_FilterNodes_AppliesPredicate
GraphProcedureBuilder_Build_ReturnsFrozenProcedure
SimpleGraphProcedure_Execute_DelegatesToTraverse
FilteredGraphProcedure_FilterEdges_SkipsLowWeight
PipelineGraphProcedure_Sequential_ChainsSteps
PipelineGraphProcedure_Union_MergesResults
PipelineGraphProcedure_Budget_StopsAtLimit
CostAccumulator_TracksAllMetrics
GraphProcedureRegistry_Register_Get_Roundtrip
BuiltIn_ExpandContext_DefaultPolicy
BuiltIn_ShortestPath_FindsPath
```

### Integration Tests

- Execute procedures against a real graph database (5K nodes, 15K edges)
- Verify results match equivalent raw `Traverse()` calls
- Verify pipeline produces correct merged results
- Verify budget enforcement stops execution within limits

### Benchmark Tests

- Procedure overhead vs raw Traverse() (target: < 1%)
- Pipeline overhead vs sequential Traverse() calls (target: < 5%)
- Built-in procedures vs hand-coded equivalents

---

## Deliverables Summary

| Phase | Deliverable | Effort |
| :--- | :--- | :--- |
| 1 | Core abstraction + simple procedures | 2-3 days |
| 2 | Filtered procedures (node/edge predicates) | 2 days |
| 3 | Pipeline procedures (multi-step) | 3 days |
| 4 | Built-in library + registry | 1-2 days |
| 5 | Benchmarks + optimization | 2 days |
| **Total** | | **10-12 days** |

---

## Success Criteria

1. **Zero overhead**: Simple procedure executes with < 1% overhead vs raw `Traverse()`
2. **Composable**: Users can chain 3+ steps in a pipeline
3. **Observable**: Every execution reports nodes/edges/tokens/time via `ICostObserver`
4. **Reusable**: Build once, execute thousands of times with different start keys
5. **Budget-safe**: Pipeline stops within token/time budget
6. **Ship with recipes**: 6 built-in procedures cover 80% of common use cases
