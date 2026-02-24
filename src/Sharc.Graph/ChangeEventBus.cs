// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// In-memory implementation of <see cref="IChangeNotifier"/>.
/// Dispatches <see cref="ChangeEvent"/> to subscribers filtered by <see cref="ConceptKind"/>.
/// Thread-safe for subscribe/unsubscribe; publish is synchronous on the caller's thread.
/// </summary>
/// <remarks>
/// F-4: Change Event Bus. Subscribers are invoked synchronously in registration order.
/// Exceptions in handlers are not caught — callers should guard their handlers.
/// </remarks>
public sealed class ChangeEventBus : IChangeNotifier
{
    private readonly object _lock = new();
    private long _nextTokenId;

    // kind-specific subscriptions
    private readonly Dictionary<ConceptKind, List<Subscription>> _kindSubs = new();

    // wildcard subscriptions (receive all events)
    private readonly List<Subscription> _allSubs = new();

    /// <inheritdoc/>
    public SubscriptionToken Subscribe(ConceptKind kind, Action<ChangeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            var token = new SubscriptionToken(Interlocked.Increment(ref _nextTokenId));
            var sub = new Subscription(token, handler);

            if (!_kindSubs.TryGetValue(kind, out var list))
            {
                list = new List<Subscription>();
                _kindSubs[kind] = list;
            }
            list.Add(sub);

            return token;
        }
    }

    /// <inheritdoc/>
    public SubscriptionToken SubscribeAll(Action<ChangeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            var token = new SubscriptionToken(Interlocked.Increment(ref _nextTokenId));
            _allSubs.Add(new Subscription(token, handler));
            return token;
        }
    }

    /// <inheritdoc/>
    public void Unsubscribe(SubscriptionToken token)
    {
        lock (_lock)
        {
            foreach (var list in _kindSubs.Values)
                list.RemoveAll(s => s.Token == token);

            _allSubs.RemoveAll(s => s.Token == token);
        }
    }

    /// <inheritdoc/>
    public void Publish(ConceptKind kind, ChangeEvent changeEvent)
    {
        List<Subscription>? kindList;
        List<Subscription> allList;

        lock (_lock)
        {
            _kindSubs.TryGetValue(kind, out kindList);
            kindList = kindList != null ? new List<Subscription>(kindList) : null;
            allList = new List<Subscription>(_allSubs);
        }

        // Dispatch outside lock to avoid deadlocks
        if (kindList != null)
        {
            foreach (var sub in kindList)
                sub.Handler(changeEvent);
        }

        foreach (var sub in allList)
            sub.Handler(changeEvent);
    }

    private sealed record Subscription(SubscriptionToken Token, Action<ChangeEvent> Handler);
}
