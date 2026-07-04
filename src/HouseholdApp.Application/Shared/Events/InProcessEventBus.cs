using HouseholdApp.Application.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Shared.Events;

public sealed record EventHandlerDescriptor(
    Type EventType,
    Func<IDomainEvent, IServiceProvider, CancellationToken, Task> Dispatch,
    bool Transactional = false);

internal sealed class InProcessEventBus(
    IServiceProvider sp,
    IEnumerable<EventHandlerDescriptor> descriptors) : IEventBus
{
    // Per-phase dequeue (not a shared list) so a reentrant Flush*Async call — e.g. Marten's
    // ExpenseEventPublishingListener flushing from inside SaveChangesAsync — can't redeliver an
    // event an outer Flush*Async is still processing.
    private readonly Queue<IDomainEvent> _transactionalQueue = new();
    private readonly Queue<IDomainEvent> _deferredQueue = new();

    private readonly Dictionary<Type, List<EventHandlerDescriptor>> _transactional =
        descriptors.Where(d => d.Transactional)
            .GroupBy(d => d.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

    private readonly Dictionary<Type, List<EventHandlerDescriptor>> _deferred =
        descriptors.Where(d => !d.Transactional)
            .GroupBy(d => d.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

    public void Enqueue(IDomainEvent @event)
    {
        _transactionalQueue.Enqueue(@event);
        _deferredQueue.Enqueue(@event);
    }

    public async Task FlushTransactionalAsync(CancellationToken ct = default)
    {
        while (_transactionalQueue.TryDequeue(out var @event))
            await Dispatch(_transactional, @event, ct);
    }

    public async Task FlushDeferredAsync(CancellationToken ct = default)
    {
        while (_deferredQueue.TryDequeue(out var @event))
            await Dispatch(_deferred, @event, ct);
    }

    private async Task Dispatch(
        Dictionary<Type, List<EventHandlerDescriptor>> handlersByType, IDomainEvent @event, CancellationToken ct)
    {
        if (!handlersByType.TryGetValue(@event.GetType(), out var handlers)) return;
        foreach (var h in handlers)
            await h.Dispatch(@event, sp, ct);
    }
}
