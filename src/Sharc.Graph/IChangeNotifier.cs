// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// Token returned by <see cref="IChangeNotifier.Subscribe"/> that can be used
/// to unsubscribe from change notifications.
/// </summary>
public readonly record struct SubscriptionToken(long Id);

/// <summary>
/// Notifies subscribers of graph data mutations (create, update, delete).
/// Subscribers can filter by <see cref="ConceptKind"/> or receive all events.
/// </summary>
/// <remarks>
/// F-4: Change Event Bus. Enables reactive workflows where downstream consumers
/// (search indexers, cache invalidators, UI layers) react to graph mutations
/// without polling.
/// </remarks>
public interface IChangeNotifier
{
    /// <summary>
    /// Subscribes to change events for a specific <see cref="ConceptKind"/>.
    /// </summary>
    /// <param name="kind">The concept kind to filter on.</param>
    /// <param name="handler">Callback invoked synchronously when a matching event is published.</param>
    /// <returns>A token that can be passed to <see cref="Unsubscribe"/> to stop receiving events.</returns>
    SubscriptionToken Subscribe(ConceptKind kind, Action<ChangeEvent> handler);

    /// <summary>
    /// Subscribes to all change events regardless of concept kind.
    /// </summary>
    /// <param name="handler">Callback invoked synchronously for every published event.</param>
    /// <returns>A token that can be passed to <see cref="Unsubscribe"/> to stop receiving events.</returns>
    SubscriptionToken SubscribeAll(Action<ChangeEvent> handler);

    /// <summary>
    /// Removes a subscription. The handler will no longer be invoked for new events.
    /// </summary>
    /// <param name="token">The token returned by <see cref="Subscribe"/> or <see cref="SubscribeAll"/>.</param>
    void Unsubscribe(SubscriptionToken token);

    /// <summary>
    /// Publishes a change event to all matching subscribers.
    /// </summary>
    /// <param name="kind">The concept kind of the changed record.</param>
    /// <param name="changeEvent">The event payload.</param>
    void Publish(ConceptKind kind, ChangeEvent changeEvent);
}
