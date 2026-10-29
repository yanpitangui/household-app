using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.UnitTests.Shared;

public sealed class InProcessEventBusTests
{
    private sealed record TestEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

    private static (InProcessEventBus Bus, List<IDomainEvent> Transactional, List<IDomainEvent> Deferred) Build()
    {
        var transactional = new List<IDomainEvent>();
        var deferred = new List<IDomainEvent>();
        var descriptors = new[]
        {
            new EventHandlerDescriptor(typeof(TestEvent),
                (e, _, _) => { transactional.Add(e); return Task.CompletedTask; }, Transactional: true),
            new EventHandlerDescriptor(typeof(TestEvent),
                (e, _, _) => { deferred.Add(e); return Task.CompletedTask; }),
        };
        var bus = new InProcessEventBus(new ServiceCollection().BuildServiceProvider(), descriptors);
        return (bus, transactional, deferred);
    }

    private static TestEvent NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Test]
    public async Task Discard_drops_queued_events_before_any_flush()
    {
        var (bus, transactional, deferred) = Build();
        bus.Enqueue(NewEvent());
        bus.Discard();

        await bus.FlushTransactionalAsync();
        await bus.FlushDeferredAsync();

        await Assert.That(transactional.Count).IsEqualTo(0);
        await Assert.That(deferred.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Discard_drops_only_events_still_queued_and_the_bus_keeps_working()
    {
        var (bus, transactional, deferred) = Build();
        var delivered = NewEvent();
        bus.Enqueue(delivered);
        await bus.FlushTransactionalAsync();

        bus.Discard();
        var next = NewEvent();
        bus.Enqueue(next);
        await bus.FlushTransactionalAsync();
        await bus.FlushDeferredAsync();

        await Assert.That(transactional).IsEquivalentTo(new IDomainEvent[] { delivered, next });
        await Assert.That(deferred).IsEquivalentTo(new IDomainEvent[] { next });
    }
}
