// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Sharc.Graph.Model;
using Sharc.Graph.Store;

namespace Sharc.Graph;

/// <summary>
/// A pre-compiled graph traversal handle that owns its own cursors and traversal state,
/// enabling concurrent traversals from the same <see cref="SharcContextGraph"/>.
/// Created via <see cref="SharcContextGraph.PrepareTraversal(TraversalPolicy)"/>.
/// </summary>
/// <remarks>
/// <para>Each <see cref="PreparedTraversal"/> instance owns independent edge cursors
/// and traversal collections, so multiple instances can execute concurrently without
/// interference — unlike direct <see cref="SharcContextGraph.Traverse"/> calls which
/// share state.</para>
/// <para>This type is <b>not thread-safe</b>. Each instance should be used from a single thread.</para>
/// </remarks>
public sealed class PreparedTraversal : IDisposable
{
    private readonly TraversalPolicy _policy;
    private ConceptStore? _concepts;
    private RelationStore? _relations;

    // OWNED cursors — not shared with SharcContextGraph
    private IEdgeCursor? _outgoingCursor;
    private IEdgeCursor? _incomingCursor;
    private bool _cursorsCreated;

    // OWNED traversal state — enables concurrent traversals
    private readonly HashSet<NodeKey> _visited;
    private readonly Queue<TraversalQueueItem> _queue;
    private readonly List<TraversalNode> _resultNodes;
    private readonly List<TraversalQueueItem> _collectedKeys;
    private readonly List<PathReconstructionNode> _pathNodes;

    // Pre-computed from policy
    private readonly int _capacityHint;

    internal PreparedTraversal(
        ConceptStore concepts,
        RelationStore relations,
        TraversalPolicy policy)
    {
        _concepts = concepts;
        _relations = relations;
        _policy = policy;

        _capacityHint = EstimateCapacity(policy);
        _visited = new HashSet<NodeKey>(_capacityHint);
        _queue = new Queue<TraversalQueueItem>();
        _resultNodes = new List<TraversalNode>();
        _collectedKeys = new List<TraversalQueueItem>();
        _pathNodes = new List<PathReconstructionNode>();
    }

    /// <summary>
    /// Executes the prepared traversal starting from the given node key.
    /// Uses the policy captured at prepare time and owned cursors/state.
    /// </summary>
    /// <param name="startKey">The node to start traversal from.</param>
    /// <returns>A <see cref="GraphResult"/> containing all discovered nodes.</returns>
    /// <exception cref="ObjectDisposedException">The prepared traversal has been disposed.</exception>
    public GraphResult Execute(NodeKey startKey)
    {
        ObjectDisposedException.ThrowIf(_concepts is null, this);

        var policy = _policy;
        var concepts = _concepts!;
        var relations = _relations!;

        // Clear collections for reuse
        _resultNodes.Clear();
        _visited.Clear();
        _queue.Clear();
        _collectedKeys.Clear();
        _pathNodes.Clear();

        _visited.EnsureCapacity(_capacityHint);

        bool trackPaths = policy.IncludePaths;
        RelationKind? filterKind = policy.Kind;

        int startPathIndex = -1;
        if (trackPaths)
        {
            startPathIndex = _pathNodes.Count;
            _pathNodes.Add(new PathReconstructionNode(startKey, -1));
        }

        _queue.Enqueue(new TraversalQueueItem(startKey, 0, startPathIndex));
        _visited.Add(startKey);

        // Create cursors lazily on first execution
        if (!_cursorsCreated)
        {
            if (policy.Direction == TraversalDirection.Outgoing || policy.Direction == TraversalDirection.Both)
                _outgoingCursor = relations.CreateEdgeCursor(startKey, filterKind);

            if (policy.Direction == TraversalDirection.Incoming || policy.Direction == TraversalDirection.Both)
                _incomingCursor = relations.CreateIncomingEdgeCursor(startKey, filterKind);

            _cursorsCreated = true;
        }

        var outgoingCursor = _outgoingCursor;
        var incomingCursor = _incomingCursor;

        // Timeout: compute deadline once, check periodically
        long? deadlineTicks = policy.Timeout.HasValue
            ? Stopwatch.GetTimestamp() + (long)(policy.Timeout.Value.TotalSeconds * Stopwatch.Frequency)
            : null;
        int iterCount = 0;

        // ── Phase 1: Edge-only BFS ──
        bool stoppedEarly = false;
        while (_queue.Count > 0)
        {
            if (deadlineTicks.HasValue && (++iterCount & 63) == 0
                && Stopwatch.GetTimestamp() >= deadlineTicks.Value)
                break;

            var item = _queue.Dequeue();
            _collectedKeys.Add(item);

            if (item.Key == policy.StopAtKey && _visited.Count > 1) { stoppedEarly = true; break; }

            if (policy.MaxDepth.HasValue && item.Depth >= policy.MaxDepth.Value) continue;

            int fanOutCount = 0;

            if (outgoingCursor != null)
            {
                outgoingCursor.Reset(item.Key.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(outgoingCursor, false, item.Depth, item.PathIndex, ref fanOutCount);
            }

            if (incomingCursor != null)
            {
                incomingCursor.Reset(item.Key.Value, filterKind != null ? (int)filterKind.Value : null);
                ProcessCursor(incomingCursor, true, item.Depth, item.PathIndex, ref fanOutCount);
            }
        }

        // Collect remaining queued nodes if not stopped early
        if (!stoppedEarly)
        {
            while (_queue.Count > 0)
                _collectedKeys.Add(_queue.Dequeue());
        }

        // ── Phase 2: Batch node lookup ──
        int tokenBudget = policy.MaxTokens ?? int.MaxValue;
        int tokensUsed = 0;
        bool hasTypeFilter = policy.TargetTypeFilter.HasValue;
        int targetType = policy.TargetTypeFilter.GetValueOrDefault();

        foreach (var item in _collectedKeys)
        {
            var record = concepts.Get(item.Key, policy.IncludeData);
            if (!record.HasValue) continue;

            if (hasTypeFilter && record.Value.TypeId != targetType) continue;

            if (policy.MaxTokens.HasValue)
            {
                tokensUsed += record.Value.Tokens;
                if (tokensUsed > tokenBudget) break;
            }

            IReadOnlyList<NodeKey>? path = trackPaths ? ReconstructPath(item.PathIndex) : null;
            _resultNodes.Add(new TraversalNode(record.Value, item.Depth, path));
        }

        return new GraphResult(_resultNodes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _outgoingCursor?.Dispose();
        _incomingCursor?.Dispose();
        _outgoingCursor = null;
        _incomingCursor = null;
        _concepts = null;
        _relations = null;
    }

    private void ProcessCursor(
        IEdgeCursor cursor, bool isIncoming, int currentDepth, int currentPathIndex, ref int fanOutCount)
    {
        while (cursor.MoveNext())
        {
            if (_policy.MinWeight.HasValue && cursor.Weight < _policy.MinWeight.Value) continue;
            if (_policy.MaxFanOut.HasValue && fanOutCount >= _policy.MaxFanOut.Value) break;

            var nextKey = new NodeKey(isIncoming ? cursor.OriginKey : cursor.TargetKey);
            if (_visited.Add(nextKey))
            {
                int nextPathIndex = -1;
                if (_policy.IncludePaths)
                {
                    nextPathIndex = _pathNodes.Count;
                    _pathNodes.Add(new PathReconstructionNode(nextKey, currentPathIndex));
                }

                _queue.Enqueue(new TraversalQueueItem(nextKey, currentDepth + 1, nextPathIndex));
                fanOutCount++;
            }
        }
    }

    private List<NodeKey> ReconstructPath(int index)
    {
        int count = 0;
        int walk = index;
        while (walk >= 0)
        {
            count++;
            walk = _pathNodes[walk].ParentIndex;
        }

        var path = new List<NodeKey>(count);
        for (int i = 0; i < count; i++) path.Add(default);

        walk = index;
        for (int i = count - 1; i >= 0; i--)
        {
            path[i] = _pathNodes[walk].Key;
            walk = _pathNodes[walk].ParentIndex;
        }

        return path;
    }

    private static int EstimateCapacity(TraversalPolicy policy)
    {
        if (policy.MaxDepth.HasValue && policy.MaxFanOut.HasValue)
        {
            int estimate = 1;
            for (int d = 0; d < policy.MaxDepth.Value && estimate < 4096; d++)
                estimate *= policy.MaxFanOut.Value;
            return Math.Min(estimate, 4096);
        }
        return 128;
    }
}
