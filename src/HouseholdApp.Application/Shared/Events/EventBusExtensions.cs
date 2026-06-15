using HouseholdApp.Application.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Shared.Events;

public static class EventBusExtensions
{
    public static IServiceCollection AddEventBus(this IServiceCollection services)
    {
        services.AddScoped<IEventBus, InProcessEventBus>();
        return services;
    }

    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new EventHandlerDescriptor(
            typeof(TEvent),
            (evt, sp, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)evt, ct)));
        return services;
    }

    public static async Task PublishAllAsync(this IEventBus bus, AggregateRoot aggregate, CancellationToken ct = default)
    {
        foreach (var @event in aggregate.DomainEvents)
            await bus.PublishAsync(@event, ct);
        aggregate.ClearEvents();
    }
}
