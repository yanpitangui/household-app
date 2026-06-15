using HouseholdApp.Application.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Shared.Events;

public sealed record EventHandlerDescriptor(
    Type EventType,
    Func<IDomainEvent, IServiceProvider, CancellationToken, Task> Dispatch);

internal sealed class InProcessEventBus(
    IServiceProvider sp,
    IEnumerable<EventHandlerDescriptor> descriptors) : IEventBus
{
    private readonly Dictionary<Type, List<EventHandlerDescriptor>> _byType =
        descriptors
            .GroupBy(d => d.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

    public async Task PublishAsync(IDomainEvent @event, CancellationToken ct = default)
    {
        if (!_byType.TryGetValue(@event.GetType(), out var handlers)) return;
        foreach (var h in handlers)
            await h.Dispatch(@event, sp, ct);
    }
}
