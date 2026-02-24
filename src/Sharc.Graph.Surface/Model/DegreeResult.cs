// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Model;

/// <summary>
/// Degree centrality result for a single node.
/// </summary>
/// <param name="Key">The node key.</param>
/// <param name="InDegree">Number of incoming edges.</param>
/// <param name="OutDegree">Number of outgoing edges.</param>
/// <param name="TotalDegree">Sum of in-degree and out-degree.</param>
public readonly record struct DegreeResult(NodeKey Key, int InDegree, int OutDegree, int TotalDegree);
