using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Shared.Events;

public interface IEventBus
{
    /// <summary>Queues an event for dispatch. Nothing runs until a Flush*Async call.</summary>
    void Enqueue(IDomainEvent @event);

    /// <summary>
    /// Dispatches queued events to <see cref="ITransactionalEventHandler{TEvent}"/> handlers.
    /// Called by IUnitOfWork.CommitAsync before the database transaction is committed, so these
    /// handlers can participate in the same transaction. Does not clear the queue.
    /// </summary>
    Task FlushTransactionalAsync(CancellationToken ct = default);

    /// <summary>
    /// Dispatches queued events to <see cref="IEventHandler{TEvent}"/> handlers, then clears the queue.
    /// Called by IUnitOfWork.CommitAsync after the database transaction is committed.
    /// </summary>
    Task FlushDeferredAsync(CancellationToken ct = default);
}

/// <summary>Default handler kind. Dispatched after the publishing transaction commits.</summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Opt-in handler kind for handlers that must run inside the same database transaction as the
/// event's publisher (e.g. writing to the same IUnitOfWork connection). Dispatched before commit.
/// </summary>
public interface ITransactionalEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
