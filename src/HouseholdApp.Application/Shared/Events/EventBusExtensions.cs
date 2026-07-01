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

    /// <summary>Registers a handler dispatched after the publisher's transaction (if any) commits.</summary>
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

    /// <summary>Registers a handler dispatched before the publisher's transaction commits (same tx).</summary>
    public static IServiceCollection AddTransactionalEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, ITransactionalEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new EventHandlerDescriptor(
            typeof(TEvent),
            (evt, sp, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)evt, ct),
            Transactional: true));
        return services;
    }

    /// <summary>Queues an aggregate's events for dispatch by the surrounding IUnitOfWork.CommitAsync.</summary>
    public static void EnqueueAll(this IEventBus bus, AggregateRoot aggregate)
    {
        foreach (var @event in aggregate.DomainEvents)
            bus.Enqueue(@event);
        aggregate.ClearEvents();
    }

    /// <summary>
    /// Enqueues and immediately flushes both handler kinds. For publishers with no surrounding
    /// IUnitOfWork (e.g. Marten-based command services, where SaveChangesAsync is already the
    /// commit boundary) — there's no separate before/after-commit moment to hook into.
    /// </summary>
    public static async Task PublishAllAsync(this IEventBus bus, AggregateRoot aggregate, CancellationToken ct = default)
    {
        bus.EnqueueAll(aggregate);
        await bus.FlushTransactionalAsync(ct);
        await bus.FlushDeferredAsync(ct);
    }

    /// <summary>Same as <see cref="PublishAllAsync"/> but for a single event not owned by an aggregate.</summary>
    public static async Task PublishAsync(this IEventBus bus, IDomainEvent @event, CancellationToken ct = default)
    {
        bus.Enqueue(@event);
        await bus.FlushTransactionalAsync(ct);
        await bus.FlushDeferredAsync(ct);
    }
}
