// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public sealed class ChangeEventBusTests
{
    private static ChangeEvent MakeEvent(ChangeType type, long key) =>
        new(type, new RecordId("_concepts", $"id_{key}", new NodeKey(key)), null, null);

    [Fact]
    public void Subscribe_ReceivesMatchingEvents()
    {
        var bus = new ChangeEventBus();
        var received = new List<ChangeEvent>();
        bus.Subscribe(ConceptKind.File, e => received.Add(e));

        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Create, 1));

        Assert.Single(received);
        Assert.Equal(ChangeType.Create, received[0].Type);
    }

    [Fact]
    public void Subscribe_DoesNotReceiveNonMatchingKind()
    {
        var bus = new ChangeEventBus();
        var received = new List<ChangeEvent>();
        bus.Subscribe(ConceptKind.File, e => received.Add(e));

        bus.Publish(ConceptKind.Method, MakeEvent(ChangeType.Create, 1));

        Assert.Empty(received);
    }

    [Fact]
    public void Subscribe_MultipleHandlers_AllReceive()
    {
        var bus = new ChangeEventBus();
        int count1 = 0, count2 = 0;
        bus.Subscribe(ConceptKind.Class, _ => count1++);
        bus.Subscribe(ConceptKind.Class, _ => count2++);

        bus.Publish(ConceptKind.Class, MakeEvent(ChangeType.Update, 1));

        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var bus = new ChangeEventBus();
        var received = new List<ChangeEvent>();
        var token = bus.Subscribe(ConceptKind.File, e => received.Add(e));

        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Create, 1));
        bus.Unsubscribe(token);
        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Delete, 2));

        Assert.Single(received);
    }

    [Fact]
    public void SubscribeAll_ReceivesAllKinds()
    {
        var bus = new ChangeEventBus();
        var received = new List<ChangeEvent>();
        bus.SubscribeAll(e => received.Add(e));

        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Create, 1));
        bus.Publish(ConceptKind.Method, MakeEvent(ChangeType.Update, 2));
        bus.Publish(ConceptKind.Class, MakeEvent(ChangeType.Delete, 3));

        Assert.Equal(3, received.Count);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = new ChangeEventBus();
        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Create, 1));
    }

    [Fact]
    public void Publish_DeleteEvent_ReceivesCorrectType()
    {
        var bus = new ChangeEventBus();
        ChangeEvent? captured = null;
        bus.Subscribe(ConceptKind.GitCommit, e => captured = e);

        bus.Publish(ConceptKind.GitCommit, MakeEvent(ChangeType.Delete, 99));

        Assert.NotNull(captured);
        Assert.Equal(ChangeType.Delete, captured.Value.Type);
        Assert.Equal(99, captured.Value.Id.Key.Value);
    }

    [Fact]
    public void SubscribeAll_AndKindSubscription_BothReceive()
    {
        var bus = new ChangeEventBus();
        var allEvents = new List<ChangeEvent>();
        var fileEvents = new List<ChangeEvent>();

        bus.SubscribeAll(e => allEvents.Add(e));
        bus.Subscribe(ConceptKind.File, e => fileEvents.Add(e));

        bus.Publish(ConceptKind.File, MakeEvent(ChangeType.Create, 1));
        bus.Publish(ConceptKind.Class, MakeEvent(ChangeType.Create, 2));

        Assert.Equal(2, allEvents.Count);
        Assert.Single(fileEvents);
    }
}
