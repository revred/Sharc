// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Cypher;

/// <summary>
/// Direction of an edge pattern in Cypher.
/// </summary>
internal enum CypherDirection
{
    /// <summary>Outgoing edge direction.</summary>
    Outgoing,
    /// <summary>Incoming edge direction.</summary>
    Incoming,
    /// <summary>Bidirectional edge.</summary>
    Both
}

/// <summary>
/// A node pattern: (variable:Label)
/// </summary>
internal sealed class CypherNodePattern
{
    public string? Variable { get; set; }
}

/// <summary>
/// A relationship pattern: -[variable:KIND*..N]->
/// </summary>
internal sealed class CypherRelPattern
{
    public string? Variable { get; set; }
    public int? Kind { get; set; }
    public CypherDirection Direction { get; set; } = CypherDirection.Outgoing;
    public bool IsVariableLength { get; set; }
    public int? MaxHops { get; set; }
}

/// <summary>
/// A WHERE constraint: variable.property = value
/// </summary>
internal sealed class CypherWhereClause
{
    public string Variable { get; set; } = "";
    public string Property { get; set; } = "";
    public long Value { get; set; }
}

/// <summary>
/// A complete MATCH statement with optional WHERE and RETURN.
/// </summary>
internal sealed class CypherMatchStatement
{
    public CypherNodePattern StartNode { get; set; } = new();
    public CypherRelPattern? Relationship { get; set; }
    public CypherNodePattern? EndNode { get; set; }
    public List<CypherWhereClause> WhereConstraints { get; set; } = new();
    public List<string> ReturnVariables { get; set; } = new();
    public bool IsShortestPath { get; set; }

    /// <summary>
    /// The path variable name for shortestPath patterns (e.g., "p" in "MATCH p = shortestPath(...)").
    /// </summary>
    public string? PathVariable { get; set; }
}
