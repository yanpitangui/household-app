using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Shared.Events;

public interface IEventBus
{
    Task PublishAsync(IDomainEvent @event, CancellationToken ct = default);
}

public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
