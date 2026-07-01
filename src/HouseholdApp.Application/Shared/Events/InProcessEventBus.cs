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
    private readonly List<IDomainEvent> _queue = [];

    private readonly Dictionary<Type, List<EventHandlerDescriptor>> _transactional =
        descriptors.Where(d => d.Transactional)
            .GroupBy(d => d.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

    private readonly Dictionary<Type, List<EventHandlerDescriptor>> _deferred =
        descriptors.Where(d => !d.Transactional)
            .GroupBy(d => d.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

    public void Enqueue(IDomainEvent @event) => _queue.Add(@event);

    public async Task FlushTransactionalAsync(CancellationToken ct = default)
    {
        foreach (var @event in _queue)
            await Dispatch(_transactional, @event, ct);
    }

    public async Task FlushDeferredAsync(CancellationToken ct = default)
    {
        foreach (var @event in _queue)
            await Dispatch(_deferred, @event, ct);
        _queue.Clear();
    }

    private async Task Dispatch(
        Dictionary<Type, List<EventHandlerDescriptor>> handlersByType, IDomainEvent @event, CancellationToken ct)
    {
        if (!handlersByType.TryGetValue(@event.GetType(), out var handlers)) return;
        foreach (var h in handlers)
            await h.Dispatch(@event, sp, ct);
    }
}
