// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Cypher;

/// <summary>
/// Compiles a <see cref="CypherMatchStatement"/> AST into a <see cref="CypherPlan"/>
/// that maps to existing Traverse()/ShortestPath() calls.
/// </summary>
internal static class CypherCompiler
{
    internal static CypherPlan Compile(CypherMatchStatement stmt)
    {
        var plan = new CypherPlan
        {
            IsShortestPath = stmt.IsShortestPath,
            ReturnVariables = stmt.ReturnVariables
        };

        // Build traversal direction from relationship pattern
        var direction = TraversalDirection.Outgoing;
        int? maxDepth = 1;
        RelationKind? kind = null;

        if (stmt.Relationship != null)
        {
            direction = stmt.Relationship.Direction switch
            {
                CypherDirection.Outgoing => TraversalDirection.Outgoing,
                CypherDirection.Incoming => TraversalDirection.Incoming,
                CypherDirection.Both => TraversalDirection.Both,
                _ => TraversalDirection.Outgoing
            };

            if (stmt.Relationship.Kind.HasValue)
                kind = (RelationKind)stmt.Relationship.Kind.Value;

            if (stmt.Relationship.IsVariableLength)
                maxDepth = stmt.Relationship.MaxHops; // null means unlimited
            else
                maxDepth = 1; // single hop
        }

        plan.Policy = new TraversalPolicy
        {
            Direction = direction,
            MaxDepth = maxDepth,
            Kind = kind,
            IncludeData = true
        };

        // Resolve WHERE constraints to start/end keys
        foreach (var where in stmt.WhereConstraints)
        {
            if (where.Property.Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                // Match variable to start or end node
                if (stmt.StartNode?.Variable != null &&
                    where.Variable.Equals(stmt.StartNode.Variable, StringComparison.OrdinalIgnoreCase))
                {
                    plan.StartKey = new NodeKey(where.Value);
                }
                else if (stmt.EndNode?.Variable != null &&
                    where.Variable.Equals(stmt.EndNode.Variable, StringComparison.OrdinalIgnoreCase))
                {
                    plan.EndKey = new NodeKey(where.Value);
                }
            }
        }

        return plan;
    }
}
