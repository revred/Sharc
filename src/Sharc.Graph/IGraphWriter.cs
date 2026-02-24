// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Sharc.Graph.Schema;

namespace Sharc.Graph;

/// <summary>
/// Provides typed write operations for graph nodes (concepts) and edges (relations).
/// Translates graph operations into INSERT/UPDATE/DELETE on the underlying schema tables.
/// </summary>
public interface IGraphWriter : IDisposable
{
    /// <summary>
    /// Interns a concept node. If a node with the given key already exists, returns its key unchanged.
    /// Otherwise, inserts a new node and returns the key.
    /// </summary>
    /// <param name="id">String identifier (GUID) for the node.</param>
    /// <param name="key">Integer key for the node.</param>
    /// <param name="kind">The concept kind discriminator.</param>
    /// <param name="jsonData">JSON payload for the node.</param>
    /// <param name="nodeAlias">Optional alias for the node.</param>
    /// <param name="tokens">Optional estimated token count.</param>
    /// <returns>The <see cref="NodeKey"/> of the interned node.</returns>
    NodeKey Intern(string id, NodeKey key, ConceptKind kind, string jsonData = "{}", string? nodeAlias = null, int? tokens = null);

    /// <summary>
    /// Creates an edge between two nodes.
    /// </summary>
    /// <param name="id">String identifier (GUID) for the edge.</param>
    /// <param name="origin">The source node key.</param>
    /// <param name="target">The target node key.</param>
    /// <param name="kind">The relation kind discriminator.</param>
    /// <param name="jsonData">JSON payload for the edge.</param>
    /// <param name="weight">Relevance weight (0.0–1.0).</param>
    /// <returns>The rowid of the inserted edge.</returns>
    long Link(string id, NodeKey origin, NodeKey target, RelationKind kind, string jsonData = "{}", float weight = 1.0f);

    /// <summary>
    /// Removes a concept node by its key. Also removes all edges connected to this node.
    /// </summary>
    /// <param name="key">The node key to remove.</param>
    /// <returns>True if the node was found and removed.</returns>
    bool Remove(NodeKey key);

    /// <summary>
    /// Removes an edge by its rowid.
    /// </summary>
    /// <param name="edgeRowId">The rowid of the edge to remove.</param>
    /// <returns>True if the edge was found and removed.</returns>
    bool Unlink(long edgeRowId);

    /// <summary>
    /// Commits any pending changes. For auto-commit writers, this is a no-op.
    /// </summary>
    void Commit();
}
